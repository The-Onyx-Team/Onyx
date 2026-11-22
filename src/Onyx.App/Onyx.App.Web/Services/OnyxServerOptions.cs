namespace Onyx.App.Web.Services;

/// <summary>Settings for running your own Onyx server (section <c>Onyx</c>, e.g. <c>Onyx__AllowRegistration</c>).</summary>
public sealed class OnyxServerOptions
{
    public const string Section = "Onyx";

    /// <summary>
    /// Whether new accounts can be created, with a password or through an external login. Turn it off once your
    /// own accounts exist; existing users can still sign in.
    /// </summary>
    public bool AllowRegistration { get; set; } = true;

    public const string RegistrationClosedMessage = "Registration is disabled on this server.";
}
