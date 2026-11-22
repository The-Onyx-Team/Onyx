using Microsoft.AspNetCore.Components;
using Onyx.App.Shared.Services;

namespace Onyx.App.Web.Services;

/// <summary>The web app is the server, so its address is fixed.</summary>
public class WebServerSettings(NavigationManager navigationManager) : IServerSettings
{
    public bool IsConfigurable => false;

    public string ServerUrl => navigationManager.BaseUri.TrimEnd('/');

    public Task<string?> SetServerUrlAsync(string url) =>
        throw new NotSupportedException("The web app can't change its server.");
}
