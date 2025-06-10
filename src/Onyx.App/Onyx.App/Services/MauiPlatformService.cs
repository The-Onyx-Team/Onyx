using Onyx.App.Shared.Services;

namespace Onyx.App.Services;

public class MauiPlatformService : IPlatformService
{
    public bool IsMaui() => true;
}