using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using Onyx.App.Shared.Services;
using Onyx.App.Shared.Services.Client;
using Onyx.App.Shared.Services.Usage;
using Onyx.App.Web.Client.Services;
using Onyx.Collector.Core;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();
builder.Services.AddOnyxClient();

// The server is the page's own origin.
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<BrowserCredentialStore>();
builder.Services.AddScoped<ICredentialStore>(sp => sp.GetRequiredService<BrowserCredentialStore>());
builder.Services.AddScoped<IVaultKeyOptions>(sp => sp.GetRequiredService<BrowserCredentialStore>());
builder.Services.AddScoped<BrowserCrypto>();
builder.Services.AddScoped(sp => new OnyxApiClient(
    sp.GetRequiredService<HttpClient>(),
    sp.GetRequiredService<ICredentialStore>(),
    sp.GetRequiredService<ILoggerFactory>().CreateLogger<OnyxApiClient>())
{
    Crypto = sp.GetRequiredService<BrowserCrypto>()
});

builder.Services.AddScoped<IStorage, BrowserStorage>();
builder.Services.AddScoped<IPlatformService, BrowserPlatformService>();
builder.Services.AddScoped<IServerSettings, BrowserServerSettings>();
builder.Services.AddScoped<IStatsService, BrowserStatsService>();
builder.Services.AddScoped<IFileSaver, BrowserFileSaver>();
builder.Services.AddScoped<IIconFetcher, ProxyIconFetcher>();

await builder.Build().RunAsync();
