using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Onyx.App.Shared.Services;
using Onyx.Data.ApiSchema;

namespace Onyx.App.Web.Services;

/// <summary>The web app is the server, so its address is fixed.</summary>
public class WebServerSettings(NavigationManager navigationManager, IOptionsSnapshot<OnyxServerOptions> options)
    : IServerSettings
{
    public bool IsConfigurable => false;

    public string ServerUrl => navigationManager.BaseUri.TrimEnd('/');

    public Task<string?> SetServerUrlAsync(string url) =>
        throw new NotSupportedException("The web app can't change its server.");

    public Task<ServerInfoDto?> GetInfoAsync() => Task.FromResult<ServerInfoDto?>(Info(options.Value));

    public static ServerInfoDto Info(OnyxServerOptions options) => new(
        string.IsNullOrWhiteSpace(options.Operator) ? null : options.Operator.Trim(),
        string.IsNullOrWhiteSpace(options.ContactEmail) ? null : options.ContactEmail.Trim(),
        options.AllowRegistration);
}
