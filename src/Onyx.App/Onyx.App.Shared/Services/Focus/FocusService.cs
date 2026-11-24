using Onyx.App.Shared.Services.Client;
using Onyx.App.Shared.Services.Usage;
using Onyx.Collector.Core.Crypto;
using Onyx.Collector.Core.Reminders;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Focus;

/// <summary>Apps, categories and devices to pick from, with the names to show.</summary>
public sealed record FocusChoices(
    IReadOnlyList<AppGroup> Apps,
    IReadOnlyList<string> Categories,
    IReadOnlyList<DeviceDto> Devices);

/// <summary>
/// Reminders and focus mode, kept in the encrypted settings. The dashboards edit them; the apps and the Linux agent
/// read them and notify on the device (<see cref="ReminderEngine"/>).
/// </summary>
public sealed class FocusService(VaultService vault, AppCatalog catalog, IDeviceManager devices, TimeProvider time)
{
    public async Task<VaultSettings> GetAsync(bool refresh = false) => await vault.GetSettingsAsync(refresh);

    public async Task<FocusChoices> GetChoicesAsync()
    {
        var groups = (await catalog.GetGroupsAsync()).Where(g => !g.Hidden).ToList();
        var categories = (await vault.GetSettingsAsync()).Categories.Select(c => c.Name)
            .Concat(groups.Select(g => g.Category).OfType<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        return new FocusChoices(groups, categories, await devices.GetDevices());
    }

    public Task SaveReminderAsync(ReminderSettings reminder) => vault.UpdateSettingsAsync(settings =>
    {
        var index = settings.Reminders.FindIndex(r => r.Id == reminder.Id);
        if (index >= 0)
            settings.Reminders[index] = reminder;
        else
            settings.Reminders.Add(reminder);
    });

    public Task DeleteReminderAsync(string id) =>
        vault.UpdateSettingsAsync(settings => settings.Reminders.RemoveAll(r => r.Id == id));

    public Task SetReminderEnabledAsync(string id, bool enabled) => vault.UpdateSettingsAsync(settings =>
    {
        if (settings.Reminders.Find(r => r.Id == id) is { } reminder)
            reminder.Enabled = enabled;
    });

    /// <summary>Turns focus mode on for every device until <paramref name="until"/>.</summary>
    public Task StartAsync(DateTimeOffset until) => vault.UpdateSettingsAsync(settings => settings.Focus.Until = until);

    /// <summary>Ends focus mode started by hand (schedules stay).</summary>
    public Task StopAsync() => vault.UpdateSettingsAsync(settings => settings.Focus.Until = null);

    /// <summary>Saves what is distracting, the devices and the schedules (keeping the current session).</summary>
    public Task SaveFocusAsync(AppSelection distracting, List<int> deviceIds, List<FocusSchedule> schedules) =>
        vault.UpdateSettingsAsync(settings =>
        {
            settings.Focus.Distracting = distracting;
            settings.Focus.Devices = deviceIds;
            settings.Focus.Schedules = schedules;
        });

    public DateTimeOffset Now => time.GetUtcNow();

    /// <summary>"After 1 h without a break on YouTube and Instagram, on Pixel 9".</summary>
    public static string Describe(ReminderSettings reminder, FocusChoices choices)
    {
        var time = ReminderEngine.Format(TimeSpan.FromMinutes(reminder.Minutes));
        var what = reminder.Kind == ReminderKind.Continuous
            ? $"After {time} without a break"
            : $"When {time} a day is reached";
        return $"{what} {DescribeApps(reminder.Apps, choices)}{DescribeDevices(reminder.Devices, choices)}";
    }

    public static string DescribeApps(AppSelection selection, FocusChoices choices)
    {
        if (selection.IsEmpty)
            return "on any app";
        var names = selection.Categories.Select(c => $"{c} apps")
            .Concat(AppNames(selection, choices))
            .ToList();
        return "on " + Join(names);
    }

    public static string DescribeDevices(List<int> deviceIds, FocusChoices choices) =>
        deviceIds.Count == 0
            ? ""
            : ", only on " + Join(deviceIds.Select(id => choices.Devices.FirstOrDefault(d => d.Id == id)?.Name ?? $"device {id}").ToList());

    /// <summary>The shown names of the apps a selection holds (each app once, even with several identifiers).</summary>
    public static List<string> AppNames(AppSelection selection, FocusChoices choices) =>
        choices.Apps.Where(g => g.Members.Any(m => selection.Apps.Contains(m.Identifier)))
            .Select(g => g.Name)
            .Concat(selection.Apps.Where(id => !choices.Apps.Any(g => g.Members.Any(m => m.Identifier == id)))
                .Select(id => id.Split(':', 2).Last()))
            .ToList();

    private static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}"
    };
}
