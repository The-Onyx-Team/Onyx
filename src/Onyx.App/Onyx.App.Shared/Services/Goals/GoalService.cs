using Onyx.App.Shared.Services.Client;
using Onyx.App.Shared.Services.Usage;
using Onyx.Collector.Core.Crypto;

namespace Onyx.App.Shared.Services.Goals;

/// <summary>The user's goals (kept in the encrypted settings) and how they stand.</summary>
public sealed class GoalService(VaultService vault, IUsageDataService usage, IDeviceManager devices)
{
    public async Task<IReadOnlyList<GoalSettings>> GetGoalsAsync() => (await vault.GetSettingsAsync()).Goals;

    public async Task<List<GoalProgress>> EvaluateAsync(TimeZoneInfo zone, DateOnly today)
    {
        var goals = await GetGoalsAsync();
        if (goals.Count == 0)
            return [];

        var from = today.AddDays(-GoalEvaluator.HistoryDays - 7).ToDateTime(TimeOnly.MinValue);
        var rows = await usage.GetUsageDataAsync(TimeZoneInfo.ConvertTimeToUtc(from, zone), DateTime.UtcNow.AddHours(2));
        var names = (await devices.GetDevices()).ToDictionary(d => d.Id, d => d.Name);
        return goals.Select(g => GoalEvaluator.Evaluate(g, rows, zone, today, names)).ToList();
    }

    public Task SaveAsync(GoalSettings goal) => vault.UpdateSettingsAsync(settings =>
    {
        var index = settings.Goals.FindIndex(g => g.Id == goal.Id);
        if (index >= 0)
            settings.Goals[index] = goal;
        else
            settings.Goals.Add(goal);
    });

    public Task DeleteAsync(string goalId) => vault.UpdateSettingsAsync(settings =>
        settings.Goals.RemoveAll(g => g.Id == goalId));
}
