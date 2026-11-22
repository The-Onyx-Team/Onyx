using Onyx.App.Shared.Services.Auth;
using Onyx.App.Shared.Services.Usage;
using Onyx.Collector.Core;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Services.Api;

public class ApiProfileService(OnyxApiClient client) : IProfileService
{
    public Task<ProfileDto> GetAsync() => client.GetProfileAsync();

    public Task<string?> UpdateAsync(string userName, string? phoneNumber) =>
        Try(() => client.UpdateProfileAsync(userName, phoneNumber));

    public Task<string?> SetAvatarAsync(byte[] data, string contentType) =>
        Try(() => client.SetAvatarAsync(data, contentType));

    public Task<string?> DeleteAvatarAsync() => Try(() => client.DeleteAvatarAsync());

    private static async Task<string?> Try(Func<Task> call)
    {
        try
        {
            await call();
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or NotSignedInException or TaskCanceledException)
        {
            return OnyxApiClient.ErrorMessage(e);
        }
    }
}

public class ApiReportService(OnyxApiClient client) : IReportService
{
    public Task<WeeklyReportDto> GetWeeklyReportAsync(DateOnly weekStart, TimeZoneInfo zone) =>
        client.GetWeeklyReportAsync(weekStart, zone.Id);
}
