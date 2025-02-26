using Microsoft.EntityFrameworkCore;
using Onyx.App.Web.Components;
using Onyx.Data.DataBaseSchema;
using static Provider;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Base
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Database

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var provider = config.GetValue("provider", SQLite.Name);
    if (provider == SqlServer.Name)
    {
        options.UseSqlServer(
            config.GetConnectionString(SqlServer.Name)!,
            x => x.MigrationsAssembly(SqlServer.Assembly)
        );
    }
    else if (provider == SQLite.Name || builder.Environment.IsDevelopment())
    {
        options.UseSqlite(
            config.GetConnectionString(SQLite.Name)!,
            x => x.MigrationsAssembly(SQLite.Assembly)
        );
    }
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Onyx.App.Shared._Imports).Assembly);

app.Run();

public record Provider(string Name, string Assembly)
{
    public static Provider SQLite = new(nameof(SQLite), typeof(Onyx.Data.SQLite.Marker).Assembly.GetName().Name!);

    public static Provider SqlServer =
        new(nameof(SqlServer), typeof(Onyx.Data.SqlServer.Marker).Assembly.GetName().Name!);
}