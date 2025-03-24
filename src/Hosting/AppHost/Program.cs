var builder = DistributedApplication.CreateBuilder(args);

var dbPassword = builder.AddParameter("dbPassword");

var db = builder.AddSqlServer("db", dbPassword)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var webApp = builder.AddProject<Projects.Onyx_App_Web>("webapp")
    .WithReference(db)
    .WithEnvironment("provider", "SqlServer");

builder.Build().Run();
