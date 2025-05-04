namespace AppHost.Resources;

public static class MailDevResourceBuilderExtensions
{
    public static IResourceBuilder<MailDevResource> AddMailDev(
        this IDistributedApplicationBuilder builder,
        string name,
        int? httpPort = null,
        int? smtpPort = null)
    {
        var resource = new MailDevResource(name);

        return builder.AddResource(resource)
            .WithImage(MailDevContainerImageTags.Image)
            .WithImageRegistry(MailDevContainerImageTags.Registry)
            .WithImageTag(MailDevContainerImageTags.Tag)
            .WithHttpEndpoint(
                targetPort: 1080,
                port: httpPort,
                name: MailDevResource.HttpEndpointName)
            .WithEndpoint(
                targetPort: 1025,
                port: smtpPort,
                name: MailDevResource.SmtpEndpointName);
    }
}

internal static class MailDevContainerImageTags
{
    internal const string Registry = "docker.io";

    internal const string Image = "maildev/maildev";

    internal const string Tag = "2.1.0";
}

public sealed class MailDevResource(string name) : ContainerResource(name), IResourceWithConnectionString
{
    internal const string SmtpEndpointName = "smtp";
    internal const string HttpEndpointName = "http";

    private EndpointReference? m_SmtpReference;

    public EndpointReference SmtpEndpoint =>
        m_SmtpReference ??= new(this, SmtpEndpointName);

    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create(
            $"smtp://{SmtpEndpoint.Property(EndpointProperty.Host)}:{SmtpEndpoint.Property(EndpointProperty.Port)}"
        );
}