namespace Onyx.App.Shared.Services.Usage;

public interface IStatsService
{
    void OpenUsageAccessSettings();

    Task<bool> UploadData();
}