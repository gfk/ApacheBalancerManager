using ApacheBalancerWasmInterface;
using ApacheBalancerWasmInterface.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Radzen;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Base URL of the Apache Balancer BFF; configured in wwwroot/appsettings.json.
string bffBaseUrl = builder.Configuration["BffBaseUrl"] ?? builder.HostEnvironment.BaseAddress;

builder.Services.AddScoped((IServiceProvider serviceProvider) => new HttpClient
{
    BaseAddress = new Uri(bffBaseUrl)
});
builder.Services.AddScoped<BalancerApiClient>();
builder.Services.AddScoped<ConsoleLogService>();
builder.Services.AddScoped<WorkerMetricsHistory>();
builder.Services.AddRadzenComponents();

await builder.Build().RunAsync();
