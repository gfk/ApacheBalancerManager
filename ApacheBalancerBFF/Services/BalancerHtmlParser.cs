using System.Globalization;
using Shared.Dtos;
using HtmlAgilityPack;

namespace ApacheBalancerBFF.Services;

/// <summary>
/// Default <see cref="IBalancerHtmlParser"/> built on HtmlAgilityPack.
///
/// Expected DOM shape (Apache 2.4 balancer-manager), repeated once per pool as siblings in body:
///   h3   → "LoadBalancer Status for [a href='...?b={name}&amp;nonce={uuid}']balancer://{name}[/a]"
///   table→ pool summary (MaxMembers, StickySession, DisableFailover, Timeout, FailoverAttempts, Method, Path, Active)
///   table→ workers (Worker URL, Route, RouteRedir, Factor, Set, Status, Elected, Busy, Load, To, From,
///                   then optionally HC Method, HC Interval, Passes, Fails, HC uri, HC Expr)
/// </summary>
public sealed class BalancerHtmlParser : IBalancerHtmlParser
{
    private const string BalancerHeadingXPath = "//h3[a[contains(@href, 'balancer-manager?b=')]]";
    private const int WorkerColumnsWithoutHealthCheck = 11;
    private const int WorkerColumnsWithHealthCheck = 17;

    private readonly ILogger<BalancerHtmlParser> _logger;

    public BalancerHtmlParser(ILogger<BalancerHtmlParser> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public List<BalancerStatusDto> ParseBalancers(string html)
    {
        List<BalancerStatusDto> balancers = new();

        HtmlDocument document = new();
        document.LoadHtml(html);

        HtmlNodeCollection? headings = document.DocumentNode.SelectNodes(BalancerHeadingXPath);
        if (headings is null || headings.Count == 0)
        {
            _logger.LogWarning("No balancer headings found in the page; the balancer-manager HTML layout may have changed.");
            return balancers;
        }

        foreach (HtmlNode heading in headings)
        {
            BalancerStatusDto? balancer = ParseBalancerSection(heading);
            if (balancer is not null)
            {
                balancers.Add(balancer);
            }
        }

        return balancers;
    }

    private BalancerStatusDto? ParseBalancerSection(HtmlNode heading)
    {
        HtmlNode? anchor = heading.SelectSingleNode("a");
        if (anchor is null)
        {
            _logger.LogWarning("Balancer heading without anchor, skipping: {Heading}", Truncate(heading.OuterHtml));
            return null;
        }

        string href = anchor.GetAttributeValue("href", string.Empty);
        Dictionary<string, string> queryParameters = ParseQueryString(href);

        if (!queryParameters.TryGetValue("b", out string? balancerName) || string.IsNullOrEmpty(balancerName))
        {
            _logger.LogWarning("Could not extract balancer name ('b' parameter) from heading anchor, skipping: {Href}", href);
            return null;
        }

        queryParameters.TryGetValue("nonce", out string? nonce);
        if (string.IsNullOrEmpty(nonce))
        {
            _logger.LogWarning("No nonce found for balancer {Balancer}; modifications will not be possible.", balancerName);
        }

        BalancerStatusDto balancer = new()
        {
            Name = balancerName,
            Nonce = nonce ?? string.Empty
        };

        (HtmlNode? summaryTable, HtmlNode? workersTable) = FindBalancerTables(heading);

        if (summaryTable is not null)
        {
            PopulateSummary(balancer, summaryTable);
        }
        else
        {
            _logger.LogWarning("No summary table found for balancer {Balancer}.", balancerName);
        }

        if (workersTable is not null)
        {
            balancer.Workers = ParseWorkers(workersTable, balancerName);
        }
        else
        {
            _logger.LogWarning("No workers table found for balancer {Balancer}.", balancerName);
        }

        return balancer;
    }

    /// <summary>Walks the heading's following siblings, collecting the two tables of the section (stops at the next h3).</summary>
    private static (HtmlNode? SummaryTable, HtmlNode? WorkersTable) FindBalancerTables(HtmlNode heading)
    {
        List<HtmlNode> tables = new();
        HtmlNode? sibling = heading.NextSibling;

        while (sibling is not null && tables.Count < 2)
        {
            if (string.Equals(sibling.Name, "h3", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (string.Equals(sibling.Name, "table", StringComparison.OrdinalIgnoreCase))
            {
                tables.Add(sibling);
            }

            sibling = sibling.NextSibling;
        }

        HtmlNode? summaryTable = tables.Count > 0 ? tables[0] : null;
        HtmlNode? workersTable = tables.Count > 1 ? tables[1] : null;
        return (summaryTable, workersTable);
    }

    private void PopulateSummary(BalancerStatusDto balancer, HtmlNode summaryTable)
    {
        HtmlNode? dataRow = summaryTable.SelectSingleNode(".//tr[td]");
        if (dataRow is null)
        {
            _logger.LogWarning("Summary table of balancer {Balancer} has no data row.", balancer.Name);
            return;
        }

        HtmlNodeCollection? cells = dataRow.SelectNodes("td");
        if (cells is null || cells.Count < 8)
        {
            _logger.LogWarning(
                "Summary table of balancer {Balancer} has {Count} cells, expected 8: {Row}",
                balancer.Name,
                cells?.Count ?? 0,
                Truncate(dataRow.OuterHtml));
            return;
        }

        balancer.MaxMembers = CellText(cells[0]);
        balancer.StickySession = CellText(cells[1]);
        balancer.DisableFailover = CellText(cells[2]);
        balancer.Timeout = CellText(cells[3]);
        balancer.FailoverAttempts = CellText(cells[4]);
        balancer.Method = CellText(cells[5]);
        balancer.Path = CellText(cells[6]);
        balancer.Active = CellText(cells[7]);
    }

    private List<WorkerStatusDto> ParseWorkers(HtmlNode workersTable, string balancerName)
    {
        List<WorkerStatusDto> workers = new();

        HtmlNodeCollection? rows = workersTable.SelectNodes(".//tr[td]");
        if (rows is null)
        {
            _logger.LogWarning("Workers table of balancer {Balancer} has no data rows.", balancerName);
            return workers;
        }

        foreach (HtmlNode row in rows)
        {
            HtmlNodeCollection? cells = row.SelectNodes("td");
            if (cells is null || cells.Count < WorkerColumnsWithoutHealthCheck)
            {
                _logger.LogWarning(
                    "Worker row in balancer {Balancer} has {Count} cells, expected at least {Expected}; skipping row: {Row}",
                    balancerName,
                    cells?.Count ?? 0,
                    WorkerColumnsWithoutHealthCheck,
                    Truncate(row.OuterHtml));
                continue;
            }

            WorkerStatusDto worker = new();

            HtmlNode? urlAnchor = cells[0].SelectSingleNode(".//a");
            worker.Url = urlAnchor is not null ? CellText(urlAnchor) : CellText(cells[0]);

            worker.Route = CellText(cells[1]);
            worker.RouteRedir = CellText(cells[2]);

            string factorText = CellText(cells[3]);
            if (double.TryParse(factorText, NumberStyles.Float, CultureInfo.InvariantCulture, out double loadFactor))
            {
                worker.LoadFactor = loadFactor;
            }
            else
            {
                _logger.LogWarning("Could not parse load factor '{Factor}' for worker {Worker} in balancer {Balancer}.", factorText, worker.Url, balancerName);
            }

            worker.Set = CellText(cells[4]);
            worker.RawStatus = CellText(cells[5]);
            worker.StatusFlags = worker.RawStatus
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            worker.Elected = CellText(cells[6]);
            worker.Busy = CellText(cells[7]);
            worker.Load = CellText(cells[8]);
            worker.To = CellText(cells[9]);
            worker.From = CellText(cells[10]);

            if (cells.Count >= WorkerColumnsWithHealthCheck)
            {
                worker.HealthCheckMethod = CellText(cells[11]);
                worker.HealthCheckInterval = CellText(cells[12]);
                worker.HealthCheckPasses = CellText(cells[13]);
                worker.HealthCheckFails = CellText(cells[14]);
                worker.HealthCheckUri = CellText(cells[15]);
                worker.HealthCheckExpr = CellText(cells[16]);
            }

            workers.Add(worker);
        }

        return workers;
    }

    /// <summary>Extracts and decodes the query-string parameters of an (HTML-entity-encoded) href.</summary>
    private static Dictionary<string, string> ParseQueryString(string href)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

        string decodedHref = HtmlEntity.DeEntitize(href);
        int questionMarkIndex = decodedHref.IndexOf('?');
        if (questionMarkIndex < 0)
        {
            return values;
        }

        string query = decodedHref[(questionMarkIndex + 1)..];
        string[] pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);

        foreach (string pair in pairs)
        {
            int equalsIndex = pair.IndexOf('=');
            if (equalsIndex < 0)
            {
                continue;
            }

            string key = Uri.UnescapeDataString(pair[..equalsIndex]);
            string value = Uri.UnescapeDataString(pair[(equalsIndex + 1)..]);
            values[key] = value;
        }

        return values;
    }

    private static string CellText(HtmlNode cell)
    {
        return HtmlEntity.DeEntitize(cell.InnerText).Trim();
    }

    private static string Truncate(string text)
    {
        const int maxLength = 300;
        return text.Length <= maxLength ? text : text[..maxLength] + "…";
    }
}
