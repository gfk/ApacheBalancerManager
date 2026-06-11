using Shared.Dtos;

namespace ApacheBalancerBFF.Services;

/// <summary>Scrapes the Apache balancer-manager HTML page into structured DTOs.</summary>
public interface IBalancerHtmlParser
{
    /// <summary>
    /// Parses every balancer pool (heading, summary table, workers table and per-balancer nonce)
    /// found in the given balancer-manager HTML page. Tolerant of layout drift: unparseable
    /// fragments are logged and skipped rather than throwing.
    /// </summary>
    List<BalancerStatusDto> ParseBalancers(string html);
}
