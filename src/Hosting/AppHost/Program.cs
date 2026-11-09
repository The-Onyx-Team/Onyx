using AppHost.Resources;

var builder = DistributedApplication.CreateBuilder(args);

// SQL Server in a container with a persistent volume. Aspire injects the connection string as
// ConnectionStrings:db, which the web app reads when provider=SqlServer.
var db = builder.AddSqlServer("sql")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase("db");

var mailServer = builder.AddMailDev("mail");

builder.AddProject<Projects.Onyx_App_Web>("webapp")
    .WithReference(db)
    .WaitFor(db)
    .WithReference(mailServer)
    .WithEnvironment("provider", "SqlServer");

builder.Build().Run();
