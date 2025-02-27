using System.IO.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MudBlazor.Services;
using Onyx.App.Shared.Services;
using Onyx.App.Web.Api;
using Onyx.App.Web.Components;
using Onyx.App.Web.Services.Auth;
using Onyx.App.Web.Services.Database;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.Identity;
using static Provider;

// Load Key

await KeyManager.CreateKeyIfNotExists("key.rsa");
var rsa = await KeyManager.LoadKey("key.rsa");
var key = new RsaSecurityKey(rsa);

// Base
var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<IFileSystem, FileSystem>();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddHttpClient();

builder.Services.AddMudServices();

// Database

builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
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

builder.Services.AddSingleton<DbInitializer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DbInitializer>());

// Auth

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
}).AddCookie("CookieSchema", options =>
{
    options.Cookie.Name = "AuthCookie";
}).AddJwtBearer("JwtSchema", options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        IssuerSigningKey = key
    };

    options.MapInboundClaims = true;
});

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.Lockout.AllowedForNewUsers = false;
    options.SignIn.RequireConfirmedAccount = false;
    options.SignIn.RequireConfirmedEmail = false;
    options.User.RequireUniqueEmail = true;
    options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredUniqueChars = 0;
}).AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddScoped<IUserManager, UserManager>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Onyx.App.Shared._Imports).Assembly);

app.MapAuthEndpoints();

app.Run();

public record Provider(string Name, string Assembly)
{
    public static Provider SQLite = new(nameof(SQLite), typeof(Onyx.Data.SQLite.Marker).Assembly.GetName().Name!);

    public static Provider SqlServer =
        new(nameof(SqlServer), typeof(Onyx.Data.SqlServer.Marker).Assembly.GetName().Name!);
}