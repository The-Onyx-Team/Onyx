using Onyx.Data.ApiSchema;

namespace Onyx.Collector.Core.Browser;

/// <summary>Tells which website a browser window shows.</summary>
public interface IWebsiteSource
{
    /// <returns>The normalized domain, or null when <paramref name="app"/> is not a browser with the extension, the
    /// tab shows no website, or the website is excluded.</returns>
    string? GetDomain(ForegroundApp app);
}

/// <summary>
/// What the browser extensions reported last, one entry per connected extension (browser profile). Reports are
/// matched to the focused window by the browser's process id, so two browsers open side by side don't mix up.
/// </summary>
public sealed class BrowserActivity(TimeProvider timeProvider) : IWebsiteSource
{
    private sealed record Report(int BrowserProcessId, bool Focused, string? Domain, DateTimeOffset At);

    private readonly Lock m_Lock = new();
    private readonly Dictionary<long, Report> m_Reports = new();
    private IReadOnlyList<string> m_Excluded = [];

    /// <summary>Number of connected browser extensions.</summary>
    public int Connections
    {
        get
        {
            lock (m_Lock) return m_Reports.Count;
        }
    }

    /// <summary>The newest report of any extension, for status output.</summary>
    public (string? Domain, DateTimeOffset At)? LastReport
    {
        get
        {
            lock (m_Lock)
                return m_Reports.Values.MaxBy(r => r.At) is { } r ? (r.Domain, r.At) : null;
        }
    }

    /// <summary>Records the state an extension reported.</summary>
    /// <param name="connection">Identifies the extension instance (one per browser profile).</param>
    /// <param name="browserProcessId">Main process of the browser the extension runs in.</param>
    /// <param name="focused">Whether one of this profile's windows has focus.</param>
    /// <param name="domain">Domain or URL of the active tab; normalized here.</param>
    public void Update(long connection, int browserProcessId, bool focused, string? domain)
    {
        lock (m_Lock)
            m_Reports[connection] = new Report(browserProcessId, focused, WebDomain.Normalize(domain), timeProvider.GetUtcNow());
    }

    public void Disconnected(long connection)
    {
        lock (m_Lock)
            m_Reports.Remove(connection);
    }

    /// <summary>The user's excluded websites (from the server); their time is recorded without the domain.</summary>
    public void SetExcludedDomains(IEnumerable<string> domains)
    {
        var normalized = domains.Select(WebDomain.Normalize).OfType<string>().ToList();
        lock (m_Lock)
            m_Excluded = normalized;
    }

    public string? GetDomain(ForegroundApp app)
    {
        if (app.ProcessId <= 0)
            return null;

        lock (m_Lock)
        {
            // Several profiles of one browser share its process. The one whose window has focus reported last.
            var domain = m_Reports.Values
                .Where(r => r.BrowserProcessId == app.ProcessId && r.Focused)
                .MaxBy(r => r.At)?.Domain;

            return domain is not null && !WebDomain.IsExcluded(domain, m_Excluded) ? domain : null;
        }
    }
}
