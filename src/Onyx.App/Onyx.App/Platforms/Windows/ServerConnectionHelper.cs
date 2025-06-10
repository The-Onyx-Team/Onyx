namespace Onyx.App;

public static class ServerConnectionHelper
{
#if DEBUG
    public const string BaseUrl = "https://localhost:7189";
#else
    public const string BaseUrl = "https://onyx.g-martin.work";
#endif
}