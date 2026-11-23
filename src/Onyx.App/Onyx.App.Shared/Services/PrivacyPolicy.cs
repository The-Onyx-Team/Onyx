namespace Onyx.App.Shared.Services;

/// <summary>The privacy policy shown by the apps (<c>/privacy</c>). Users agree to a version of it when they
/// register; when the text changes in a way that needs new consent, raise the version and existing users are
/// asked again.</summary>
public static class PrivacyPolicy
{
    public const string Version = "2026-11-23";
}
