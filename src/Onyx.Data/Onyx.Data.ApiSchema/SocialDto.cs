namespace Onyx.Data.ApiSchema;

// Friends and groups. Users compare numbers they chose to share; the numbers are encrypted for each recipient
// with a key only the two users can derive (ECDH), so the server only relays them.

/// <param name="Id">Id of the friendship.</param>
/// <param name="Incoming">A request someone else sent to the user, waiting for their answer.</param>
/// <param name="Accepted">Both agreed; numbers are shared from then on.</param>
public record FriendDto(
    int Id,
    string UserId,
    string UserName,
    string? AvatarDataUrl,
    bool Incoming,
    bool Accepted,
    DateTime Since);

/// <param name="UserNameOrEmail">The other user's user name or email address.</param>
public record FriendRequestDto(string UserNameOrEmail);

public record GroupDto(int Id, string Name, bool IsAdmin, bool Accepted, List<GroupMemberDto> Members);

/// <param name="Accepted">False while the invitation is open.</param>
public record GroupMemberDto(string UserId, string UserName, string? AvatarDataUrl, bool Accepted, bool IsAdmin);

public record CreateGroupDto(string Name);

public record GroupInviteDto(string UserNameOrEmail);

/// <summary>Someone the user shares with: accepted friends and members of accepted groups.</summary>
public record ShareRecipientDto(string UserId, string UserName, byte[] PublicKey);

/// <param name="PeriodStart">Monday of the week the numbers belong to.</param>
public record ShareItemDto(string RecipientId, DateOnly PeriodStart, byte[] Data);

public record ShareUploadDto(List<ShareItemDto> Items);

/// <summary>Numbers someone shares with the user.</summary>
public record ReceivedShareDto(
    string OwnerId,
    string OwnerUserName,
    string? OwnerAvatarDataUrl,
    byte[] OwnerPublicKey,
    DateOnly PeriodStart,
    byte[] Data,
    DateTime UpdatedAt);

/// <summary>What the server stores about a user in plain text, for the personal data report (GDPR Art. 15).</summary>
public record PersonalDataDto(
    string UserId,
    string? UserName,
    string? Email,
    bool EmailConfirmed,
    string? PhoneNumber,
    bool TwoFactorEnabled,
    bool HasAvatar,
    DateTime? CreatedAt,
    DateTime? PrivacyConsentAt,
    string? PrivacyPolicyVersion,
    List<string> ExternalLogins,
    List<DeviceDto> Devices,
    DateTime? EncryptionSetUpAt,
    int EncryptedUsageRows,
    int EncryptedApps,
    List<FriendDto> Friends,
    List<GroupDto> Groups,
    int SharesSent,
    int SharesReceived);
