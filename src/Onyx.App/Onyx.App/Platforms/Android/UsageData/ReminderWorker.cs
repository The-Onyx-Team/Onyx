using System.Text.Json;
using Android.App;
using Android.Content;
using AndroidX.Core.App;
using AndroidX.Work;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Onyx.Collector.Core;
using Onyx.Collector.Core.Crypto;
using Onyx.Collector.Core.Reminders;

namespace Onyx.App.UsageData;

/// <summary>
/// Reminders and focus mode on Android: every 15 minutes (the shortest period WorkManager allows) the app reads
/// the recent foreground activity from Android's usage log and notifies what is due. Focus-mode nudges therefore
/// come with a delay of up to 15 minutes, unlike on the desktop.
/// </summary>
public class ReminderWorker(Context context, WorkerParameters parameters) : Worker(context, parameters)
{
    private const string UniqueName = "onyx-reminders";
    public const string ChannelId = "onyx_reminders";

    public static void Schedule(Context context)
    {
        var request = new PeriodicWorkRequest.Builder(typeof(ReminderWorker), TimeSpan.FromMinutes(15)).Build();
        WorkManager.GetInstance(context)
            .EnqueueUniquePeriodicWork(UniqueName, ExistingPeriodicWorkPolicy.Keep!, request);
    }

    public override Result DoWork()
    {
        var services = IPlatformApplication.Current?.Services;
        if (services?.GetService<AndroidReminders>() is not { } reminders)
            return Result.InvokeSuccess();
        reminders.CheckAsync().GetAwaiter().GetResult();
        return Result.InvokeSuccess();
    }
}

/// <summary>One check of reminders and focus mode. Settings and what was shown are kept in the app's private
/// storage, so checks work offline and nothing is shown twice.</summary>
public class AndroidReminders(OnyxApiClient client, ICredentialStore credentials, AndroidUsageReader reader,
    ILogger<AndroidReminders> logger)
{
    private static readonly JsonSerializerOptions s_Json = new(JsonSerializerDefaults.Web);
    private static string SettingsPath => Path.Combine(FileSystem.AppDataDirectory, "reminder-settings.json");
    private static string StatePath => Path.Combine(FileSystem.AppDataDirectory, "reminder-state.json");
    private readonly SemaphoreSlim m_Lock = new(1, 1);

    public async Task CheckAsync()
    {
        if (!AndroidUsageReader.HasUsageAccess() || !await client.IsSignedInAsync())
            return;

        await m_Lock.WaitAsync();
        try
        {
            var settings = await LoadSettingsAsync();
            if (settings is null || (settings.Reminders.Count == 0 && settings.Focus.Until is null && settings.Focus.Schedules.Count == 0))
                return;

            var now = DateTimeOffset.UtcNow;
            var zone = TimeZoneInfo.Local;
            var dayStart = TimeZoneInfo.ConvertTime(now, zone).Date;
            var from = new DateTimeOffset(dayStart, zone.GetUtcOffset(dayStart)) - TimeSpan.FromHours(1);
            var intervals = reader.ReadIntervals(from, now);

            var state = Read<ReminderState>(StatePath) ?? new ReminderState();
            var nudges = ReminderEngine.Evaluate(settings, (await credentials.LoadAsync())?.DeviceId, intervals, now, zone, state);
            await File.WriteAllTextAsync(StatePath, JsonSerializer.Serialize(state, s_Json));
            foreach (var nudge in nudges)
                Show(nudge);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Reminder check failed");
        }
        finally
        {
            m_Lock.Release();
        }
    }

    /// <summary>The settings from the server, or the last ones read when offline or locked.</summary>
    private async Task<VaultSettings?> LoadSettingsAsync()
    {
        try
        {
            if (await client.GetVaultSettingsAsync() is { } fresh)
            {
                await File.WriteAllTextAsync(SettingsPath, JsonSerializer.Serialize(fresh, s_Json));
                return fresh;
            }
        }
        catch (Exception e) when (e is HttpRequestException or NotSignedInException or TaskCanceledException)
        {
            logger.LogDebug(e, "Using the saved reminder settings");
        }

        return Read<VaultSettings>(SettingsPath);
    }

    private static T? Read<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), s_Json) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Show(Nudge nudge)
    {
        var context = Android.App.Application.Context;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
            manager.CreateNotificationChannel(new NotificationChannel(ReminderWorker.ChannelId, "Reminders and focus mode",
                NotificationImportance.Default) { Description = "Breaks, daily limits and focus mode" });
        }

        var open = context.PackageManager!.GetLaunchIntentForPackage(context.PackageName!);
        var pending = open is null ? null : PendingIntent.GetActivity(context, 0, open,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var notification = new NotificationCompat.Builder(context, ReminderWorker.ChannelId)
            .SetSmallIcon(Resource.Mipmap.appicon)!
            .SetContentTitle(nudge.Title)!
            .SetContentText(nudge.Message)!
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(nudge.Message))!
            .SetAutoCancel(true)!
            .SetContentIntent(pending)!
            .Build()!;
        // Without the permission (Android 13+) this does nothing.
        NotificationManagerCompat.From(context).Notify(nudge.Key.GetHashCode(), notification);
    }
}
