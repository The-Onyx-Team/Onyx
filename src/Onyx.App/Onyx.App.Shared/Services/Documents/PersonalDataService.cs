using System.Globalization;
using System.Text.Json;
using Onyx.App.Shared.Services.Client;
using Onyx.App.Shared.Services.Dashboard;
using Onyx.App.Shared.Services.Goals;
using Onyx.App.Shared.Services.Usage;
using Onyx.Collector.Core;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Shared.Services.Documents;

/// <summary>Everything Onyx stores about the user, readable (PDF) and machine-readable (JSON).</summary>
public sealed record PersonalDataExport(ReportDocument Document, byte[] Json);

/// <summary>
/// The personal data report (GDPR Art. 15 and 20). The server contributes what it stores in plain text; the
/// encrypted data is decrypted and summarised here, because only this device can read it.
/// </summary>
public sealed class PersonalDataService(
    OnyxApiClient api,
    VaultService vault,
    AppCatalog catalog,
    E2EUsageDataService usage,
    IServerSettings server)
{
    /// <summary>Usage before this is not searched for.</summary>
    private static readonly DateTime s_Earliest = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly JsonSerializerOptions s_Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<PersonalDataExport> CreateAsync(TimeZoneInfo zone, Action<string>? progress = null)
    {
        progress?.Invoke("Loading your account…");
        var account = await api.SendAuthorizedAsync<PersonalDataDto>(HttpMethod.Get, "/api/user/personal-data");
        var info = await server.GetInfoAsync();
        var settings = await vault.GetSettingsAsync(refresh: true);
        var groups = await catalog.GetGroupsAsync(refresh: true);

        progress?.Invoke("Decrypting your usage…");
        var rows = await usage.GetDecryptedAsync(s_Earliest, DateTime.UtcNow.AddHours(2));
        var groupByTag = await catalog.GroupsByTagAsync();
        var devices = account.Devices.ToDictionary(d => d.Id, d => d.Name);
        var now = DateTime.UtcNow;
        string Local(DateTime? utc) => utc is { } t
            ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(t, DateTimeKind.Utc), zone)
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "–";
        string Hours(double seconds) => UsageSummary.Format(TimeSpan.FromSeconds(seconds));

        progress?.Invoke("Writing the report…");
        var document = new ReportDocument
        {
            Title = "Your personal data in Onyx",
            Subtitle = $"{account.UserName} · {account.Email} · server {server.ServerUrl} · " +
                       $"created {Local(now)} ({zone.Id})"
        };

        document.Section("About this report",
                "This report lists everything the Onyx server stores about you (right of access, Art. 15 GDPR). " +
                "Your usage data, app names, settings and goals are end-to-end encrypted: the server stores them " +
                "but cannot read them. This report was put together on your device, which holds the key. The " +
                "accompanying JSON file contains the same data in a machine-readable form (data portability, " +
                "Art. 20 GDPR).")
            .Values(
                ("Operator of this server", info?.Operator),
                ("Privacy contact", info?.ContactEmail),
                ("Server location", info?.HostingLocation));

        document.Section("Account", "Stored in plain text, because the server needs it to sign you in and send emails.")
            .Values(
                ("User id", account.UserId),
                ("User name", account.UserName),
                ("Email address", $"{account.Email}{(account.EmailConfirmed ? " (confirmed)" : " (not confirmed)")}"),
                ("Phone number", account.PhoneNumber),
                ("Profile picture", account.HasAvatar ? "yes" : "no"),
                ("Two-factor authentication", account.TwoFactorEnabled ? "on" : "off"),
                ("Account created", Local(account.CreatedAt)),
                ("Privacy policy accepted",
                    account.PrivacyConsentAt is null ? "not recorded" : $"{Local(account.PrivacyConsentAt)}, version {account.PrivacyPolicyVersion}"),
                ("External logins", account.ExternalLogins.Count == 0 ? "none" : string.Join(", ", account.ExternalLogins)));

        document.Section("Encryption")
            .Values(
                ("Set up", Local(account.EncryptionSetUpAt)),
                ("Encrypted usage rows", account.EncryptedUsageRows.ToString(CultureInfo.InvariantCulture)),
                ("Encrypted apps", account.EncryptedApps.ToString(CultureInfo.InvariantCulture)))
            .Paragraph("Also stored: a salt and your key encrypted with your passphrase, and a public key that lets " +
                       "friends encrypt what they share with you. Not stored: your passphrase, your recovery key.");

        document.Section("Devices", "Device names and platforms are stored in plain text, with the time of the last upload.")
            .Table(["Device", "Platform", "Last upload"],
                account.Devices.Select(d => new[] { d.Name, d.Platform ?? "–", Local(d.LastSeen) }));

        var parts = rows.SelectMany(r => r.Payload.Parts.Select(p => (Row: r, Part: p))).ToList();
        var usageSection = document.Section("Usage");
        if (parts.Count == 0)
        {
            usageSection.Paragraph("No usage has been recorded.");
        }
        else
        {
            usageSection.Values(
                ("First recorded hour", Local(rows.Min(r => r.Hour))),
                ("Last recorded hour", Local(rows.Max(r => r.Hour))),
                ("Total screen time", Hours(parts.Sum(p => p.Part.Seconds))),
                ("Hourly records", rows.Count.ToString(CultureInfo.InvariantCulture)));
            usageSection.Paragraph("Screen time per month:");
            usageSection.Bars(parts
                .GroupBy(p => TimeZoneInfo.ConvertTimeFromUtc(p.Row.Hour, zone).ToString("yyyy-MM", CultureInfo.InvariantCulture))
                .OrderBy(g => g.Key)
                .Select(g => (g.Key, (double)g.Sum(p => p.Part.Seconds), Hours(g.Sum(p => p.Part.Seconds)))));
            usageSection.Paragraph("Screen time per device:");
            usageSection.Table(["Device", "Time"], parts
                .GroupBy(p => p.Row.DeviceId)
                .OrderByDescending(g => g.Sum(p => p.Part.Seconds))
                .Select(g => new[] { devices.GetValueOrDefault(g.Key, $"removed device #{g.Key}"), Hours(g.Sum(p => p.Part.Seconds)) }),
                1);
        }

        var timeByTag = parts.GroupBy(p => p.Row.Tag).ToDictionary(g => g.Key, g => g.Sum(p => p.Part.Seconds));
        document.Section("Apps", "Names, categories and icons as your devices reported them, and your own changes.")
            .Table(["App", "Category", "Platform", "Identifier", "Time"], groups
                .SelectMany(g => g.Members.Select(m => (Group: g, Member: m)))
                .OrderByDescending(x => timeByTag.GetValueOrDefault(x.Member.Tag))
                .Select(x => new[]
                {
                    x.Member.Name + (x.Member.Hidden ? " (hidden)" : ""),
                    x.Member.Category ?? "–",
                    x.Member.Platform,
                    x.Member.Identifier,
                    Hours(timeByTag.GetValueOrDefault(x.Member.Tag))
                }), 4);

        var websites = parts.Where(p => p.Part.Domain is not null).ToList();
        var websiteSection = document.Section("Websites");
        if (websites.Count == 0)
            websiteSection.Paragraph("No websites have been recorded.");
        else
            websiteSection.Table(["Website", "Time"], websites
                .GroupBy(p => p.Part.Domain!)
                .OrderByDescending(g => g.Sum(p => p.Part.Seconds))
                .Select(g => new[] { g.Key, Hours(g.Sum(p => p.Part.Seconds)) }), 1);

        document.Section("Settings")
            .Values(
                ("Categories", string.Join(", ", settings.Categories.Select(c => c.Name))),
                ("Excluded websites", settings.ExcludedDomains.Count == 0 ? "none" : string.Join(", ", settings.ExcludedDomains)),
                ("Shared with friends", settings.Sharing.Level switch
                {
                    Collector.Core.Crypto.ShareLevel.Nothing => "nothing",
                    Collector.Core.Crypto.ShareLevel.Total => "screen time",
                    Collector.Core.Crypto.ShareLevel.Categories => "screen time and categories",
                    _ => "screen time, categories, goals and streaks"
                }))
            .Table(["Goal", "Since"], settings.Goals.Select(g => new[]
                { GoalEvaluator.Title(g, devices), g.Since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }));

        document.Section("Friends and groups",
                $"You have sent numbers {account.SharesSent} time(s) and received {account.SharesReceived}; they are " +
                "encrypted for each recipient.")
            .Table(["Friend", "Status", "Since"], account.Friends.Select(f => new[]
            {
                f.UserName, f.Accepted ? "friends" : f.Incoming ? "asked you" : "you asked", Local(f.Since)
            }))
            .Table(["Group", "Your role", "Members"], account.Groups.Select(g => new[]
            {
                g.Name, g.IsAdmin ? "admin" : g.Accepted ? "member" : "invited", g.Members.Count(m => m.Accepted).ToString()
            }), 2);

        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            generatedAt = now,
            server = server.ServerUrl,
            account,
            settings,
            apps = groups.SelectMany(g => g.Members).Select(m => new
            {
                m.Identifier, m.ReportedName, m.ReportedCategory, name = m.Name, category = m.Category, m.Hidden
            }),
            usage = rows.SelectMany(r => r.Payload.Parts.Select(p => new
            {
                hourUtc = r.Hour,
                deviceId = r.DeviceId,
                device = devices.GetValueOrDefault(r.DeviceId),
                app = r.Payload.AppIdentifier,
                appName = groupByTag.GetValueOrDefault(r.Tag)?.Name,
                website = p.Domain,
                seconds = p.Seconds
            }))
        }, s_Json);

        return new PersonalDataExport(document, json);
    }
}
