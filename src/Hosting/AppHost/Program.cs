using AppHost.Resources;

var builder = DistributedApplication.CreateBuilder(args);

var mailServer = builder.AddMailDev("mail");

var webApp = builder.AddProject<Projects.Onyx_App_Web>("webapp")
    .WithReference(mailServer);

builder.Build().Run();
