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

builder.Services.Configure<WatchdogOptions>(
    builder.Configuration.GetSection(WatchdogOptions.SectionName));

builder.Services.AddHttpClient(BalancerManagerClient.HttpClientName, (HttpClient client) =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// The watchdog's probes carry their own per-probe deadline through a cancellation token, which is
// the whole point of it; this timeout is only a backstop well above the largest sane budget.
builder.Services.AddHttpClient(BackendProbe.HttpClientName, (HttpClient client) =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ApacheBalancerWatchdog/1.0");
})
.ConfigurePrimaryHttpMessageHandler((IServiceProvider provider) =>
    ProbeHandlerFactory.Create(provider.GetRequiredService<IOptions<WatchdogOptions>>().Value));

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

// External watchdog. It stays dormant unless "Watchdog:Enabled" is true, and in observe-only mode
// unless "Watchdog:DryRun" is explicitly false.
builder.Services.AddSingleton<WatchdogStateStore>();
builder.Services.AddSingleton<IBackendProbe, BackendProbe>();
builder.Services.AddHostedService<WatchdogService>();

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

// This service runs strictly inside an isolated management network:
// no authentication/authorization middleware, Swagger UI always on.
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();
