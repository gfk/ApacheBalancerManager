using System.Globalization;

namespace ApacheBalancerWasmInterface.Services;

/// <summary>
/// Turns the balancer-manager's display strings back into numbers so they can be plotted.
/// The BFF keeps the cells verbatim, exactly as Apache rendered them.
/// </summary>
public static class ApacheMetricParser
{
    private const double Kilobyte = 1024d;

    /// <summary>
    /// Parses a plain counter cell ("Elected", "Busy"), which Apache prints as bare digits.
    /// Anything unexpected reads as 0 rather than breaking the graph.
    /// </summary>
    public static double ParseCount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0d;
        }

        if (double.TryParse(text.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double value))
        {
            return value;
        }

        return 0d;
    }

    /// <summary>
    /// Parses a byte cell ("To"/"From"). Apache formats those with apr_strfsize: bare bytes below
    /// 973 ("512"), otherwise a 1024-based short form ("1.2K", " 45M", "1.5G"). The placeholders
    /// apr_strfsize emits for negative ("-") and overflowing ("****") sizes read as 0.
    /// </summary>
    /// <remarks>
    /// Note the precision this costs us: at the "1.2G" scale one displayed increment is ~107 MB,
    /// so the plotted curve moves in steps rather than continuously. That is Apache's resolution,
    /// not a rounding we introduce.
    /// </remarks>
    public static double ParseBytes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0d;
        }

        string trimmed = text.Trim();
        double multiplier = MultiplierFor(trimmed[^1]);
        if (multiplier > 1d)
        {
            trimmed = trimmed[..^1].Trim();
        }

        if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double value))
        {
            return value * multiplier;
        }

        return 0d;
    }

    private static double MultiplierFor(char suffix)
    {
        switch (char.ToUpperInvariant(suffix))
        {
            case 'K':
                return Kilobyte;
            case 'M':
                return Kilobyte * Kilobyte;
            case 'G':
                return Kilobyte * Kilobyte * Kilobyte;
            case 'T':
                return Kilobyte * Kilobyte * Kilobyte * Kilobyte;
            case 'P':
                return Kilobyte * Kilobyte * Kilobyte * Kilobyte * Kilobyte;
            case 'E':
                return Kilobyte * Kilobyte * Kilobyte * Kilobyte * Kilobyte * Kilobyte;
            default:
                return 1d;
        }
    }
}
