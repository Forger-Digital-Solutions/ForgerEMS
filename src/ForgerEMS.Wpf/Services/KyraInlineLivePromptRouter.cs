namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>Maps natural-language current-data prompts to Kyra live slash tools before generic chat.</summary>
public static class KyraInlineLivePromptRouter
{
    public static bool TryBuildParse(string prompt, out KyraSlashCommandParseResult parse)
    {
        if (TryBuildWeatherParse(prompt, out parse))
        {
            return true;
        }

        parse = new KyraSlashCommandParseResult();
        var trimmed = prompt.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        var lower = trimmed.ToLowerInvariant();
        if (lower.Contains("news", StringComparison.Ordinal))
        {
            parse = KyraSlashCommandParser.Parse("/news");
            return true;
        }

        if (lower.Contains("bitcoin", StringComparison.Ordinal) ||
            lower.Contains("btc", StringComparison.Ordinal) ||
            lower.Contains("crypto", StringComparison.Ordinal) ||
            lower.Contains("ethereum", StringComparison.Ordinal) ||
            lower.Contains("eth", StringComparison.Ordinal))
        {
            var symbol = lower.Contains("eth", StringComparison.Ordinal) ||
                         lower.Contains("ethereum", StringComparison.Ordinal)
                ? "ETH"
                : "BTC";
            parse = KyraSlashCommandParser.Parse("/crypto " + symbol);
            return true;
        }

        if (lower.Contains("stock", StringComparison.Ordinal) ||
            lower.Contains("ticker", StringComparison.Ordinal) ||
            lower.Contains("nasdaq", StringComparison.Ordinal) ||
            lower.Contains("nyse", StringComparison.Ordinal))
        {
            var ticker = "SPY";
            var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"\b[A-Z]{1,5}\b");
            if (match.Success)
            {
                ticker = match.Value;
            }

            parse = KyraSlashCommandParser.Parse("/stocks " + ticker);
            return true;
        }

        return false;
    }

    public static bool TryBuildWeatherParse(string prompt, out KyraSlashCommandParseResult parse)
    {
        parse = new KyraSlashCommandParseResult();
        var trimmed = prompt.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        var lower = trimmed.ToLowerInvariant();
        if (!lower.Contains("weather", StringComparison.Ordinal) &&
            !lower.Contains("forecast", StringComparison.Ordinal))
        {
            return false;
        }

        var location = ExtractWeatherLocation(trimmed);
        parse = KyraSlashCommandParser.Parse(string.IsNullOrWhiteSpace(location)
            ? "/weather"
            : "/weather " + location);
        return true;
    }

    private static string ExtractWeatherLocation(string prompt)
    {
        var patterns = new[]
        {
            @"\bweather\s+(?:today\s+)?(?:in|for|near)\s+(?<loc>.+)$",
            @"\bforecast\s+(?:today\s+)?(?:in|for|near)\s+(?<loc>.+)$",
            @"\b(?:in|for|near)\s+(?<loc>[A-Za-z][A-Za-z0-9\s,.-]{1,80})$"
        };

        foreach (var pattern in patterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                prompt,
                pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            var loc = match.Groups["loc"].Value.Trim().Trim('?', '.', '!');
            if (!string.IsNullOrWhiteSpace(loc) &&
                !loc.Equals("today", StringComparison.OrdinalIgnoreCase) &&
                !loc.Equals("right now", StringComparison.OrdinalIgnoreCase))
            {
                return loc;
            }
        }

        return string.Empty;
    }
}
