using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Onyx.Collector.Core;

public sealed class ForegroundTrackerOptions
{
    /// <summary>How often the foreground app is sampled.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>No input for this long counts as away from the device; that time is not recorded.</summary>
    public TimeSpan IdleThreshold { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How often the pending store is written to disk.</summary>
    public TimeSpan SaveInterval { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Polls the foreground app on desktop platforms and adds the observed time to a <see cref="PendingUsageStore"/>.
/// </summary>
/// <remarks>
/// Each sample credits the time since the previous sample to the app that was in front at the previous sample.
/// Gaps longer than a few poll intervals (sleep, hibernation, a stalled process) are not credited.
/// </remarks>
public sealed class ForegroundTracker(
    IForegroundAppSource foregroundSource,
    IIdleTimeSource? idleSource,
    PendingUsageStore store,
    TimeProvider timeProvider,
    ForegroundTrackerOptions? options = null,
    ILogger? logger = null)
{
    private readonly ForegroundTrackerOptions m_Options = options ?? new ForegroundTrackerOptions();
    private readonly ILogger m_Logger = logger ?? NullLogger.Instance;

    private ForegroundApp? m_Current;
    private DateTimeOffset m_LastSample;
    private DateTimeOffset m_LastSave;
    private bool m_LoggedFailure;

    /// <summary>The app sampled last, for status output.</summary>
    public ForegroundApp? Current => m_Current;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(m_Options.PollInterval, timeProvider);
        m_LastSample = m_LastSave = timeProvider.GetUtcNow();

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                Sample();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Sample();
            store.Save();
        }
    }

    /// <summary>Takes one sample. Public so tests and hosts with their own loop can drive the tracker.</summary>
    public void Sample()
    {
        var now = timeProvider.GetUtcNow();
        var elapsed = now - m_LastSample;

        if (m_Current is not null && elapsed > TimeSpan.Zero && elapsed <= m_Options.PollInterval * 3)
            store.Add(m_Current, m_LastSample, now);

        m_LastSample = now;
        m_Current = ReadForegroundApp();

        if (now - m_LastSave >= m_Options.SaveInterval)
        {
            store.Save();
            m_LastSave = now;
        }
    }

    private ForegroundApp? ReadForegroundApp()
    {
        try
        {
            if (idleSource?.GetIdleTime() is { } idle && idle >= m_Options.IdleThreshold)
                return null;

            var app = foregroundSource.GetForegroundApp();
            m_LoggedFailure = false;
            return app;
        }
        catch (Exception e)
        {
            // Processes exit between "which window" and "which process", access is denied for elevated
            // processes, ... A failed sample must never stop the loop. Log once per failure streak.
            if (!m_LoggedFailure)
                m_Logger.LogWarning(e, "Could not determine the foreground app");
            m_LoggedFailure = true;
            return null;
        }
    }
}
