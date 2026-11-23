namespace Onyx.App.Shared.Services;

public interface IPlatformService
{
    bool IsMaui();

    /// <summary>Where this UI runs, for the About page, e.g. "Android 15 (Pixel 9)" or "Web browser".</summary>
    string Description => IsMaui() ? "App" : "Web browser";
}
