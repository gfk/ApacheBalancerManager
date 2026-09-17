#:sdk Microsoft.NET.Sdk.Web
//
// fake-balancer-manager — stands in for the /balancer-manager pages of a set of Apache servers so
// the dashboard can be run, and looked at, without a proxy cluster to point it at.
//
// Why it exists: every row in the dashboard comes from scraped Apache HTML, so with no reachable
// server the grid is empty — there is nothing to see and nothing to click. Reviewing a change to
// the worker table otherwise means having a two-proxy cluster at hand, or trusting a screenshot.
//
// It serves what BalancerHtmlParser actually reads: an h3 whose anchor carries b= and a nonce,
// followed by the summary table and the worker table as its siblings. The seeded workers cover
// every status flag the dashboard paints — Ok, Dis, Drn, Stby, Err — so each button state is on
// screen at once. POSTs apply the w_status_* fields the same way mod_proxy_balancer does, which
// means the toggles hold and the whole nonce → POST → confirm path in the BFF gets exercised
// rather than stubbed.
//
// Run it (.NET 10 runs a single file directly, no project needed):
//
//   ASPNETCORE_URLS="http://localhost:8801;http://localhost:8802" dotnet run tools/fake-balancer-manager.cs
//
// Then start the BFF against it, overriding the configured servers from the environment so no
// appsettings file has to be edited:
//
//   ApacheManagement__Servers__0__Id=lb-01 ApacheManagement__Servers__0__BaseUrl=http://localhost:8801 \
//   ApacheManagement__Servers__1__Id=lb-02 ApacheManagement__Servers__1__BaseUrl=http://localhost:8802 \
//   dotnet run --project ApacheBalancerBFF
//
// Each listening port is its own server, whichever ports you bind: the first port to be asked for
// a page is seeded as 10.0.0.11, the second as 10.0.0.21, and so on, each with its own copy of the
// pools. A worker can therefore be disabled on one server while staying healthy on the other,
// which is the case the pool-first grid exists to show.
//
// This is a development fixture and nothing else: state is in memory, there is no authentication,
// and the numbers are invented. It is not a mod_proxy_balancer emulator — it implements the parts
// of the page this dashboard reads, and no more.

using System.Text;
using Microsoft.Extensions.Primitives;

// Seed layout, one entry per worker: the flags it starts in. Between them the two pools cover
// every status the dashboard paints, so no clicking is needed to see each button state.
(string Pool, string Route, bool Disabled, bool Draining, bool Standby, bool Error)[] seedRows =
[
    ("web-frontend", "node1", false, false, false, false),
    ("web-frontend", "node2", false, false, false, false),
    ("web-frontend", "node3", true, false, false, false),
    ("web-frontend", "node4", false, true, false, false),
    ("web-frontend", "node5", false, false, true, false),
    ("api-backend", "api1", false, false, false, false),
    ("api-backend", "api2", false, false, true, false),
    ("api-backend", "api3", false, false, false, true)
];

Dictionary<int, List<Worker>> serversByPort = new();
object seedLock = new();

// Servers are seeded on first contact rather than from a fixed port list, so the fixture works on
// whatever ASPNETCORE_URLS binds it to instead of silently serving an empty page.
List<Worker> WorkersOf(int port)
{
    lock (seedLock)
    {
        if (serversByPort.TryGetValue(port, out List<Worker>? existing))
        {
            return existing;
        }

        string host = $"10.0.0.{serversByPort.Count + 1}1";
        List<Worker> workers = seedRows
            .Select((row, index) => new Worker(
                row.Pool,
                $"http://{host}:{8080 + index}",
                row.Route,
                row.Disabled,
                row.Draining,
                row.Standby,
                row.Error))
            .ToList();

        serversByPort[port] = workers;
        return workers;
    }
}

string RenderPage(int port)
{
    List<Worker> workers = WorkersOf(port);

    StringBuilder html = new();
    html.Append("<html><head><title>Balancer Manager</title></head><body>");
    html.Append("<h1>Load Balancer Manager for fake-balancer-manager</h1>");

    foreach (string pool in workers.Select((Worker worker) => worker.Pool).Distinct())
    {
        // Apache mints a fresh nonce on every render and the BFF scrapes it right before posting;
        // handing out a new one each time keeps that round trip honest.
        string nonce = Guid.NewGuid().ToString();
        html.Append($"<hr/><h3><a href=\"/balancer-manager?b={pool}&amp;nonce={nonce}\">balancer://{pool}</a></h3>");

        List<Worker> members = workers.Where((Worker worker) => worker.Pool == pool).ToList();

        html.Append("<table><tr><th>MaxMembers</th><th>StickySession</th><th>DisableFailover</th><th>Timeout</th>");
        html.Append("<th>FailoverAttempts</th><th>Method</th><th>Path</th><th>Active</th></tr>");
        html.Append($"<tr><td>{members.Count} [{members.Count} Used]</td><td>(None)</td><td>Off</td><td>0</td>");
        html.Append("<td>1</td><td>byrequests</td><td>/</td><td>Yes</td></tr></table>");

        html.Append("<table><tr><th>Worker URL</th><th>Route</th><th>RouteRedir</th><th>Factor</th><th>Set</th>");
        html.Append("<th>Status</th><th>Elected</th><th>Busy</th><th>Load</th><th>To</th><th>From</th></tr>");

        foreach (Worker worker in members)
        {
            html.Append($"<tr><td><a href=\"/balancer-manager?b={pool}&amp;w={Uri.EscapeDataString(worker.Url)}\">{worker.Url}</a></td>");
            html.Append($"<td>{worker.Route}</td><td>&nbsp;</td><td>1</td><td>0</td>");
            html.Append($"<td>{worker.Status}</td><td>{worker.Elected}</td><td>0</td><td>{worker.LoadPercent}</td>");
            html.Append($"<td>{worker.To}</td><td>{worker.From}</td></tr>");
        }

        html.Append("</table>");
    }

    html.Append("</body></html>");
    return html.ToString();
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
WebApplication app = builder.Build();

app.MapGet("/balancer-manager", (HttpContext context) =>
    Results.Content(RenderPage(context.Connection.LocalPort), "text/html"));

app.MapPost("/balancer-manager", async (HttpContext context) =>
{
    int port = context.Connection.LocalPort;
    IFormCollection form = await context.Request.ReadFormAsync();

    string pool = form["b"].ToString();
    string url = form["w"].ToString();

    Worker? worker = WorkersOf(port)
        .FirstOrDefault((Worker candidate) => candidate.Pool == pool && candidate.Url == url);

    if (worker is null)
    {
        Console.WriteLine($"[{port}] unknown worker '{url}' in pool '{pool}'");
    }
    else
    {
        // mod_proxy_balancer only applies the w_status_* fields present in the body; a field left
        // out keeps its current value, which is what lets the BFF send one flag at a time.
        if (form.TryGetValue("w_status_D", out StringValues disabled)) worker.Disabled = disabled == "1";
        if (form.TryGetValue("w_status_N", out StringValues draining)) worker.Draining = draining == "1";
        if (form.TryGetValue("w_status_H", out StringValues standby)) worker.Standby = standby == "1";
        if (form.TryGetValue("w_status_S", out StringValues stopped)) worker.Stopped = stopped == "1";
        Console.WriteLine($"[{port}] {pool} {url} -> {worker.Status}");
    }

    // Apache answers a modification with the refreshed page, which is what the BFF re-parses to
    // confirm the change actually took.
    return Results.Content(RenderPage(port), "text/html");
});

app.Run();

/// <summary>One balancer member, holding the mutable flags the dashboard toggles.</summary>
internal sealed class Worker(string pool, string url, string route, bool disabled, bool draining, bool standby, bool error)
{
    /// <summary>Fixed seed: the invented traffic figures stay the same from run to run.</summary>
    private static readonly Random Random = new(7);

    public string Pool { get; } = pool;

    public string Url { get; } = url;

    public string Route { get; } = route;

    public bool Disabled { get; set; } = disabled;

    public bool Draining { get; set; } = draining;

    public bool Standby { get; set; } = standby;

    public bool Stopped { get; set; }

    public bool Error { get; } = error;

    public int Elected { get; } = Random.Next(200, 90_000);

    public string LoadPercent { get; } = Random.Next(0, 40).ToString();

    /// <summary>Apache compacts these through apr_strfsize, so they are strings, not numbers.</summary>
    public string To { get; } = $"{Random.Next(1, 900)}K";

    public string From { get; } = $"{Random.Next(1, 40)}M";

    /// <summary>The Status cell as Apache renders it: flags separated by spaces, e.g. "Init Drn Ok".</summary>
    public string Status
    {
        get
        {
            List<string> flags = ["Init"];
            if (Disabled) flags.Add("Dis");
            if (Draining) flags.Add("Drn");
            if (Standby) flags.Add("Stby");
            if (Stopped) flags.Add("Stop");
            if (Error) flags.Add("Err");
            if (!Disabled && !Stopped && !Error) flags.Add("Ok");
            return string.Join(' ', flags);
        }
    }
}
