using System.Text;
using ApacheBalancerBFF.Configuration;
using ApacheBalancerBFF.Services;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

// Apache serves balancer-manager pages as ISO-8859-1 / windows-1252; register the
// code-pages provider so HttpClient can decode them on .NET (Core) runtimes.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ApacheManagementOptions>(
    builder.Configuration.GetSection(ApacheManagementOptions.SectionName));

// Read once, here: whether the watchdog exists at all in this process is a composition-time
// decision rather than a run-time one. See the registration block further down.
IConfigurationSection watchdogSection = builder.Configuration.GetSection(WatchdogOptions.SectionName);
bool watchdogEnabled = watchdogSection.GetValue<bool>(nameof(WatchdogOptions.Enabled));

builder.Services.Configure<WatchdogOptions>(watchdogSection);

builder.Services.AddHttpClient(BalancerManagerClient.HttpClientName, (HttpClient client) =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// The optional traffic snapshot is an enhancement, not the status itself: it gets a shorter leash
// so a hung metrics endpoint cannot hold a status poll open for the full ten seconds.
builder.Services.AddHttpClient(TrafficMetricsClient.HttpClientName, (HttpClient client) =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddSingleton<IBalancerHtmlParser, BalancerHtmlParser>();
builder.Services.AddTransient<IBalancerManagerClient, BalancerManagerClient>();
// Singleton so it can remember which servers it has already complained about; see the class.
builder.Services.AddSingleton<ITrafficMetricsClient, TrafficMetricsClient>();
builder.Services.AddTransient<IBalancerOrchestrationService, BalancerOrchestrationService>();

// The external watchdog is entirely opt-in. With "Watchdog:Enabled" absent or false, nothing in
// this block is registered: no background loop, no HTTP client, no state file, nothing added to
// the container. An install that does not want it carries none of it, so the feature cannot
// change how the BFF behaves until somebody asks for it.
if (watchdogEnabled)
{
    // The probes carry their own deadline through a cancellation token, which is the whole point
    // of the watchdog; this timeout is only a backstop well above the largest sane probe budget.
    builder.Services.AddHttpClient(BackendProbe.HttpClientName, (HttpClient client) =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ApacheBalancerWatchdog/1.0");
    })
    .ConfigurePrimaryHttpMessageHandler((IServiceProvider provider) =>
        ProbeHandlerFactory.Create(provider.GetRequiredService<IOptions<WatchdogOptions>>().Value));

    builder.Services.AddSingleton<WatchdogStateStore>();
    builder.Services.AddSingleton<IBackendProbe, BackendProbe>();

    // Still observe-only unless "Watchdog:DryRun" is explicitly false.
    builder.Services.AddHostedService<WatchdogService>();
}

builder.Services
    .AddControllers()
    .AddJsonOptions((Microsoft.AspNetCore.Mvc.JsonOptions options) =>
    {
        // Keep PascalCase property names so responses match the documented contract
        // (SuccessData, Errors, ServerId, ...).
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

// The WASM dashboard is served from a different origin; this service runs strictly
// inside an isolated management network, so an open CORS policy is acceptable.
builder.Services.AddCors((Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions options) =>
{
    options.AddDefaultPolicy((Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder policy) =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen((Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions options) =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Apache Balancer Manager BFF",
        Version = "v1",
        Description = "Unified REST facade over the mod_proxy_balancer /balancer-manager pages of multiple Apache 2.4 servers."
    });

    string xmlDocumentationPath = Path.Combine(AppContext.BaseDirectory, "ApacheBalancerBFF.xml");
    if (File.Exists(xmlDocumentationPath))
    {
        options.IncludeXmlComments(xmlDocumentationPath);
    }
});

WebApplication app = builder.Build();

if (!watchdogEnabled)
{
    // Said once at startup so that an operator who expected supervision learns it is absent here,
    // rather than from the silence of a backend that was never taken out of rotation.
    app.Logger.LogInformation(
        "The external watchdog is not enabled ('{Section}:Enabled' is not true); Apache's own health checks are the only supervision.",
        WatchdogOptions.SectionName);
}

// This service runs strictly inside an isolated management network:
// no authentication/authorization middleware, Swagger UI always on.
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();
