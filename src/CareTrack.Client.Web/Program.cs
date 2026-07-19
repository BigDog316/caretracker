using CareTrack.Client.Shared.Api;
using CareTrack.Client.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The API origin comes from wwwroot/appsettings.json so deployments can
// point at their environment without a rebuild.
var apiBase = builder.Configuration["ApiBaseUrl"]
              ?? builder.HostEnvironment.BaseAddress;

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBase) });
builder.Services.AddScoped<ITokenStore, LocalStorageTokenStore>();
builder.Services.AddScoped<IFileSaver, BrowserFileSaver>();
builder.Services.AddScoped<CareTrackApi>();

await builder.Build().RunAsync();
