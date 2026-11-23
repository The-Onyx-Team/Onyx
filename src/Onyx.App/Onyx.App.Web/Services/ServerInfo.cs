using Onyx.Data.ApiSchema;

namespace Onyx.App.Web.Services;

/// <summary>The public server information behind <c>GET /api/info</c>.</summary>
public static class ServerInfo
{
    public static ServerInfoDto Create(OnyxServerOptions options, bool sendsEmail) => new(
        Clean(options.Operator),
        Clean(options.ContactEmail),
        options.AllowRegistration,
        Clean(options.HostingLocation),
        Clean(options.EmailProvider),
        sendsEmail);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
