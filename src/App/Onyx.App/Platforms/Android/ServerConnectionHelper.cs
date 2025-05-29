namespace Onyx.App;

public static class ServerConnectionHelper
{
#if DEBUG
    public const string BaseUrl = "http://10.0.2.2:5262";
#else
    public const string BaseUrl = "https://onyx.g-martin.work";
#endif
}