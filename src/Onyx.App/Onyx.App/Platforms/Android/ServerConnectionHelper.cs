namespace Onyx.App;

/// <summary>Server a fresh install uses until the user picks one on the login page (see ServerSettings).</summary>
public static class ServerConnectionHelper
{
#if DEBUG
    // The host machine, as seen from the Android emulator.
    public const string DefaultUrl = "http://10.0.2.2:5262";
#else
    public const string DefaultUrl = "https://onyx.g-martin.work";
#endif
}
