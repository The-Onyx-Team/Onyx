using Microsoft.JSInterop;

namespace Onyx.App.Shared.Services.Dashboard;

/// <summary>The viewer's time zone, as the browser or the apps' web view reports it.</summary>
public class ClientTimeZone(OnyxJs js)
{
    private TimeZoneInfo? m_Zone;

    /// <remarks>Needs JS interop, so call it from <c>OnAfterRenderAsync</c> or later.</remarks>
    public async Task<TimeZoneInfo> GetAsync()
    {
        if (m_Zone is not null)
            return m_Zone;

        try
        {
            m_Zone = TimeZoneInfo.FindSystemTimeZoneById(await js.InvokeAsync<string>("getTimeZone"));
        }
        catch (Exception e) when (e is JSException or TimeZoneNotFoundException or InvalidOperationException
                                      or InvalidTimeZoneException or JSDisconnectedException)
        {
            m_Zone = TimeZoneInfo.Local;
        }

        return m_Zone;
    }

    /// <summary>Today in the viewer's zone.</summary>
    public async Task<DateOnly> TodayAsync() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, await GetAsync()));
}
