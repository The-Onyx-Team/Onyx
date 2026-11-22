using System.IO.Abstractions;
using System.Net.Mail;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using MudBlazor.Services;
using Onyx.App.Shared.Services;
using Onyx.App.Shared.Services.Auth;
using Onyx.App.Shared.Services.Usage;
using Onyx.App.Web.Api;
using Onyx.App.Web.Components;
using Onyx.App.Web.Services;
using Onyx.App.Web.Services.Auth;
using Onyx.App.Web.Services.Data;
using Onyx.App.Web.Services.Database;
using Onyx.App.Web.Services.Mail;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.Identity;
using ServiceDefaults;
using static Onyx.App.Web.Services.Database.DatabaseProvider;

// Container health check: the runtime image has no curl, so the app checks itself ("dotnet Onyx.App.Web.dll
// --healthcheck", see the Dockerfile) and exits 0 when its liveness endpoint answers.
if (args.Contains("--healthcheck"))
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    try
    {
        var url = Environment.GetEnvironmentVariable("HEALTHCHECK_URL") ?? "http://localhost:8080/alive";
        return (await http.GetAsync(url)).IsSuccessStatusCode ? 0 : 1;
    }
    catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
    {
        return 1;
    }
}

// Base
var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// Keys: the JWT signing key and the data-protection keys must survive restarts and redeploys, otherwise every
// user is logged out. "KeyDirectory" lets a container point this at a mounted volume.

var keyDirectory = config.GetValue("KeyDirectory", ".")!;
Directory.CreateDirectory(keyDirectory);
var keyPath = Path.Combine(keyDirectory, "key.rsa");
await KeyManager.CreateKeyIfNotExists(keyPath);
var rsa = await KeyManager.LoadKey(keyPath);
var key = new RsaSecurityKey(rsa);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<IFileSystem, FileSystem>();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddHttpClient();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(keyDirectory, "keys")))
    .SetApplicationName("OnyxApp");

builder.Services.AddSingleton(new KeyAccessor(key));

// UI

builder.Services.AddMudServices();
builder.Services.AddBlazoredLocalStorage();
builder.Services.TryAddScoped<IStorage, WebStorage>();

// Database

var provider = config.GetValue("provider", SQLite.Name);

if (provider == SqlServer.Name)
{
    // "db" is the name Aspire injects the connection string under.
    // "dotnet ef migrations add" builds the model without connecting, so a placeholder is fine at design time.
    var connectionString = config.GetConnectionString(SqlServer.Name) ?? config.GetConnectionString("db")
        ?? (EF.IsDesignTime ? "Server=design-time-placeholder" : null)
        ?? throw new InvalidOperationException(
            "provider is SqlServer but neither ConnectionStrings:SqlServer nor ConnectionStrings:db is set.");

    builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
        options.UseSqlServer(connectionString, x =>
        {
            x.EnableRetryOnFailure();
            x.MigrationsAssembly(SqlServer.Assembly);
        }));
}
else if (provider == SQLite.Name)
{
    var connectionString = config.GetConnectionString(SQLite.Name)
        ?? throw new InvalidOperationException("provider is SQLite but ConnectionStrings:SQLite is not set.");

    builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
        options.UseSqlite(connectionString, x => x.MigrationsAssembly(SQLite.Assembly)));
}
else
{
    throw new InvalidOperationException($"Unknown database provider '{provider}'. Use SQLite or SqlServer.");
}

builder.Services.AddSingleton<DbInitializer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DbInitializer>());

// Auth

var authentication = builder.Services.AddAuthentication()
    .AddCookie("CookieSchema", options =>
    {
        options.Cookie.Name = "AuthCookie";
        options.Cookie.SameSite = SameSiteMode.None;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    })
    .AddJwtBearer(JwtSchema, options =>
    {
        options.TokenValidationParameters = JwtTools.CreateValidationParameters(key);
        options.MapInboundClaims = true;
    });

// External logins are optional: each provider is only added when its client id and secret are configured.
// Registering one without credentials makes every request fail with "Options.ClientId must be provided".

if (ExternalLoginCredentials(config, "Google") is { } google)
{
    authentication.AddGoogleOpenIdConnect(options =>
    {
        options.ClientId = google.Id;
        options.ClientSecret = google.Secret;
        options.SaveTokens = true;
        options.UsePkce = true;
        options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
    });
}

if (ExternalLoginCredentials(config, "Microsoft") is { } microsoft)
{
    authentication.AddMicrosoftAccount(options =>
    {
        options.ClientId = microsoft.Id;
        options.ClientSecret = microsoft.Secret;
    });
}

if (ExternalLoginCredentials(config, "GitHub") is { } gitHub)
{
    authentication.AddGitHub(options =>
    {
        options.ClientId = gitHub.Id;
        options.ClientSecret = gitHub.Secret;
        options.CallbackPath = "/signin-oidc-github";
    });
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthRateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // API endpoints only accept access tokens from the JWT scheme. Without an explicit scheme they would fall
    // back to the Identity cookie and redirect API clients to the login page.
    options.AddPolicy(ApiPolicy, x =>
        x.AddAuthenticationSchemes(JwtSchema)
            .RequireAuthenticatedUser()
            .RequireClaim(JwtTools.TokenUseKey, JwtTools.AccessTokenUse));

    options.AddPolicy("TwoFactorEnabled", x => x.RequireClaim("amr", "mfa"));
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
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, ApplicationUserClaimsPrincipalFactory>();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddScoped<IUserManager, UserManager>();
builder.Services.AddScoped<IUserProvider, UserProvider>();
builder.Services.AddScoped<IProfileService, ProfileService>();

// Platform Service
builder.Services.AddScoped<IPlatformService, WebPlatformService>();
builder.Services.AddScoped<IServerSettings, WebServerSettings>();
builder.Services.Configure<OnyxServerOptions>(config.GetSection(OnyxServerOptions.Section));

// e-mail

var smtpServer = config.GetConnectionString("mail");

if (!string.IsNullOrWhiteSpace(smtpServer))
{
    builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentitySmtpEmailSender>();
    // smtp://host:port, or smtps://user:password@host:587 for STARTTLS with a login (URL-encode the password).
    builder.Services.AddSingleton<SmtpClient>(_ => IdentitySmtpEmailSender.CreateClient(new Uri(smtpServer)));
}
else
    builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// Usage Data

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<IUsageDataService, UsageDataService>();
builder.Services.AddScoped<IDeviceManager, DeviceManager>();
builder.Services.AddScoped<IAppManager, AppManager>();
builder.Services.AddScoped<IWebsiteSettings, WebsiteSettings>();
builder.Services.AddSingleton<IStatsService, WebStatsService>();
builder.Services.AddScoped<Onyx.App.Shared.Services.Dashboard.ClientTimeZone>();
builder.Services.AddSingleton<Onyx.App.Web.Services.Reports.ReportChartRenderer>();
builder.Services.AddScoped<IReportService, Onyx.App.Web.Services.Reports.ReportService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

app.UseRouting();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapOpenApi();
app.MapDefaultEndpoints();
app.MapAuthEndpoints();
app.MapDataEndpoints();
app.MapDeviceEndpoints();
app.MapAppEndpoints();
app.MapWebsiteEndpoints();
app.MapReportEndpoints();
// Public: the apps show it on the privacy and register pages before anyone signs in.
app.MapGet("/api/info", (Microsoft.Extensions.Options.IOptionsSnapshot<OnyxServerOptions> options) =>
    WebServerSettings.Info(options.Value)).AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Onyx.App.Shared._Imports).Assembly);

app.Run();
return 0;

static (string Id, string Secret)? ExternalLoginCredentials(IConfiguration config, string provider)
{
    var id = config[$"Authentication:{provider}:ClientId"];
    var secret = config[$"Authentication:{provider}:ClientSecret"];
    return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret) ? null : (id, secret);
}

public partial class Program
{
    public const string JwtSchema = "JwtSchema";
    public const string ApiPolicy = "api";
    public const string AuthRateLimit = "auth";
}