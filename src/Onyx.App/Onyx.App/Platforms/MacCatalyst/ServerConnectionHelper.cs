namespace Onyx.App;

public static class ServerConnectionHelper
{
#if DEBUG
    public const string DefaultUrl = "https://localhost:7189";
#else
    public const string DefaultUrl = "https://onyx.g-martin.work";
#endif
}