# Database

We use EF Core Code First to manage our database schema. 
So... Migrations it is.

## Migrations

To auto-generate migrations for all database systems we support, run the following command:

```bash
dotnet ef migrations add 
    --output-dir Migrations 
    --startup-project ./src/Onyx.App/Onyx.App.Web/Onyx.App.Web.csproj 
    --project ./src/Onyx.Data/Onyx.Data.SQLite/
    --context ApplicationDbContext 
    <MigrationName> -- --provider SQLite
dotnet ef migrations add 
    --output-dir Migrations 
    --startup-project ./src/Onyx.App/Onyx.App.Web/Onyx.App.Web.csproj 
    --project ./src/Onyx.Data/Onyx.Data.SqlServer/
    --context ApplicationDbContext 
    <MigrationName> -- --provider SqlServer
```

1. Based on the solution directory
2. Make sure to replace the MigrationName with the name of your Migration (use CamelCase)
