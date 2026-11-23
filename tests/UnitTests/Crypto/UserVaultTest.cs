using System.Security.Cryptography;
using JetBrains.Annotations;
using Onyx.Collector.Core.Crypto;
using Onyx.Data.ApiSchema;
using Shouldly;
using Xunit;

namespace UnitTests.Crypto;

[TestSubject(typeof(UserVault))]
public class UserVaultTest
{
    private static readonly IOnyxCrypto s_Crypto = DotNetCrypto.Instance;

    private static async Task<(UserVault Vault, VaultDto Dto, string RecoveryKey)> CreateAsync(string passphrase = "correct horse battery")
    {
        var (vault, dto, recovery) = await UserVault.CreateAsync(s_Crypto, passphrase, VaultSettings.CreateDefault());
        return (vault, new VaultDto(dto.Salt, dto.Iterations, dto.WrappedKey, dto.PublicKey, dto.EncryptedPrivateKey,
            dto.Settings, 1, DateTime.UtcNow), recovery);
    }

    [Fact]
    public void Encrypt_RoundTrips_AndBindsTheContext()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var blob = DotNetCrypto.Encrypt(key, "hello"u8.ToArray(), "context-a");

        blob[0].ShouldBe(CryptoFormat.Version);
        blob.Length.ShouldBe(5 + CryptoFormat.Overhead);
        DotNetCrypto.Decrypt(key, blob, "context-a").ShouldBe("hello"u8.ToArray());
        Should.Throw<CryptographicException>(() => DotNetCrypto.Decrypt(key, blob, "context-b"));

        blob[^1] ^= 1;
        Should.Throw<CryptographicException>(() => DotNetCrypto.Decrypt(key, blob, "context-a"));
    }

    [Fact]
    public async Task Unlock_WithThePassphrase_GivesTheSameKeys_AndRejectsAWrongOne()
    {
        var (vault, dto, _) = await CreateAsync();

        var unlocked = await UserVault.UnlockAsync(s_Crypto, dto, "correct horse battery");
        unlocked.Keys.MasterKey.ShouldBe(vault.Keys.MasterKey);
        dto.Iterations.ShouldBe(CryptoFormat.Iterations);

        await Should.ThrowAsync<CryptographicException>(() => UserVault.UnlockAsync(s_Crypto, dto, "wrong"));
    }

    [Fact]
    public async Task RecoveryKey_UnlocksAndCanSetANewPassphrase()
    {
        var (vault, dto, recoveryKey) = await CreateAsync();
        recoveryKey.Length.ShouldBe(52 + 12); // 13 groups of four, 12 dashes
        RecoveryKeys.Decode(recoveryKey.ToLowerInvariant().Replace("-", " ")).ShouldBe(vault.Keys.MasterKey);

        var recovered = await UserVault.UnlockWithRecoveryKeyAsync(s_Crypto, dto, recoveryKey);
        var rewrapped = await recovered.RewrapAsync("a new passphrase");
        var updated = dto with { Salt = rewrapped.Salt, WrappedKey = rewrapped.WrappedKey };

        (await UserVault.UnlockAsync(s_Crypto, updated, "a new passphrase")).Keys.MasterKey.ShouldBe(vault.Keys.MasterKey);
        await Should.ThrowAsync<CryptographicException>(() => UserVault.UnlockAsync(s_Crypto, updated, "correct horse battery"));

        var other = RecoveryKeys.Encode(RandomNumberGenerator.GetBytes(32));
        await Should.ThrowAsync<CryptographicException>(() => UserVault.UnlockWithRecoveryKeyAsync(s_Crypto, dto, other));
        RecoveryKeys.Decode("not a key").ShouldBeNull();
    }

    [Fact]
    public async Task Tags_AreStablePerUser_AndDifferBetweenUsers()
    {
        var (a, _, _) = await CreateAsync();
        var (b, _, _) = await CreateAsync();

        var tag = await a.TagAsync("android:org.mozilla.firefox");
        tag.Length.ShouldBe(22);
        (await a.TagAsync("android:org.mozilla.firefox")).ShouldBe(tag);
        (await (await UserVault.FromMasterKeyAsync(s_Crypto, a.Keys.MasterKey)).TagAsync("android:org.mozilla.firefox")).ShouldBe(tag);
        (await a.TagAsync("linux:firefox")).ShouldNotBe(tag);
        (await b.TagAsync("android:org.mozilla.firefox")).ShouldNotBe(tag);
    }

    [Fact]
    public async Task Usage_RoundTrips_OneRowPerAppAndHour_WithWebsitesInside()
    {
        var (vault, _, _) = await CreateAsync();
        var hour = new DateTime(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc);
        UsageEntryDto[] entries =
        [
            new("linux:firefox", "Firefox", "Network", hour, TimeSpan.FromMinutes(10), "github.com"),
            new("linux:firefox", "Firefox", "Network", hour, TimeSpan.FromMinutes(5)),
            new("linux:code", "VS Code", "Development", hour, TimeSpan.FromMinutes(30)),
            new("linux:code", "VS Code", "Development", hour.AddHours(1), TimeSpan.FromMinutes(20)),
        ];

        var (rows, identifiers) = await vault.EncryptUsageAsync(7, entries);
        rows.Count.ShouldBe(3);
        identifiers[rows[0].Tag].ShouldBe("linux:firefox");

        var decrypted = await vault.DecryptUsageAsync(rows.Select(r => new EncryptedUsageDto(7, r.Tag, r.Hour, r.Data)).ToList());
        var firefox = decrypted.Single(d => d.Payload.AppIdentifier == "linux:firefox");
        firefox.Payload.Parts.ShouldContain(new UsagePart("github.com", 600));
        firefox.Payload.Parts.ShouldContain(new UsagePart(null, 300));
        decrypted.Where(d => d.Payload.AppIdentifier == "linux:code").Sum(d => d.Payload.Parts.Sum(p => p.Seconds)).ShouldBe(3000);
    }

    [Fact]
    public async Task Usage_MovedToAnotherHourOrDevice_DoesNotDecrypt()
    {
        var (vault, _, _) = await CreateAsync();
        var hour = new DateTime(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc);
        var (rows, _) = await vault.EncryptUsageAsync(7, [new UsageEntryDto("linux:code", "Code", null, hour, TimeSpan.FromMinutes(1))]);
        var row = rows.Single();

        (await vault.DecryptUsageAsync([new EncryptedUsageDto(7, row.Tag, hour.AddHours(1), row.Data)])).ShouldBeEmpty();
        (await vault.DecryptUsageAsync([new EncryptedUsageDto(8, row.Tag, hour, row.Data)])).ShouldBeEmpty();
        (await vault.DecryptUsageAsync([new EncryptedUsageDto(7, row.Tag, hour, row.Data)])).Count.ShouldBe(1);
    }

    [Fact]
    public async Task SettingsAndApps_RoundTrip()
    {
        var (vault, dto, _) = await CreateAsync();
        var settings = await vault.DecryptSettingsAsync(dto.Settings);
        settings.Categories.Select(c => c.Name).ShouldContain("Social");

        settings.Goals.Add(new GoalSettings { Scope = GoalScope.App, Target = "Firefox", Minutes = 90, Direction = GoalDirection.AtMost });
        settings.ExcludedDomains.Add("example.com");
        var again = await vault.DecryptSettingsAsync(await vault.EncryptSettingsAsync(settings));
        again.Goals.Single().Minutes.ShouldBe(90);
        again.ExcludedDomains.ShouldBe(["example.com"]);

        var app = await vault.EncryptAppAsync(new AppInfoPayload("android:md.obsidian", "Obsidian", "Productivity", [1, 2, 3]));
        var apps = await vault.DecryptAppsAsync([app]);
        apps[app.Tag].Name.ShouldBe("Obsidian");
        apps[app.Tag].Icon.ShouldBe(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public async Task Sharing_BothSidesDeriveTheSameKey_AndTheSafetyCodeMatches()
    {
        var (alice, aliceDto, _) = await CreateAsync();
        var (bob, bobDto, _) = await CreateAsync();
        var alicePrivate = await alice.DecryptPrivateKeyAsync(aliceDto);
        var bobPrivate = await bob.DecryptPrivateKeyAsync(bobDto);
        var context = E2EContext.Share("alice", "bob", new DateOnly(2026, 11, 16));

        var blob = await alice.EncryptForAsync(new[] { 1, 2, 3 }, alicePrivate, aliceDto.PublicKey, bobDto.PublicKey, context);
        var read = await bob.DecryptFromAsync<int[]>(blob, bobPrivate, bobDto.PublicKey, aliceDto.PublicKey, context);
        read.ShouldBe([1, 2, 3]);

        (await bob.DecryptFromAsync<int[]>(blob, bobPrivate, bobDto.PublicKey, aliceDto.PublicKey,
            E2EContext.Share("alice", "carol", new DateOnly(2026, 11, 16)))).ShouldBeNull();

        UserVault.SafetyCode(aliceDto.PublicKey, bobDto.PublicKey)
            .ShouldBe(UserVault.SafetyCode(bobDto.PublicKey, aliceDto.PublicKey));
    }
}
