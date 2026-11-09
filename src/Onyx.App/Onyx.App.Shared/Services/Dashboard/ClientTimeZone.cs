using Microsoft.JSInterop;

namespace Onyx.App.Shared.Services.Dashboard;

/// <summary>
/// The viewer's time zone. Blazor Server code runs on the server (usually UTC), so "today" and day boundaries
/// must come from the browser; in the MAUI app the web view reports the device's zone.
/// </summary>
public class ClientTimeZone(IJSRuntime js)
{
    private TimeZoneInfo? m_Zone;

    /// <remarks>Needs JS interop, so call it from <c>OnAfterRenderAsync</c> or later.</remarks>
    public async Task<TimeZoneInfo> GetAsync()
    {
        if (m_Zone is not null)
            return m_Zone;

        try
        {
            await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/Onyx.App.Shared/js/timezone.js");
            var id = await module.InvokeAsync<string>("getTimeZone");
            m_Zone = TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is JSException or TimeZoneNotFoundException or InvalidOperationException
                                      or InvalidTimeZoneException or JSDisconnectedException)
        {
            m_Zone = TimeZoneInfo.Local;
        }

        return m_Zone;
    }
}
