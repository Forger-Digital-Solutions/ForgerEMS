using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Kyra.Core;

/// <summary>Deterministic local arithmetic for short prompts (word numbers, times/plus/minus/division, percent-of).</summary>
public static class KyraSimpleMathEvaluator
{
    private const int MaxPromptLength = 220;

    /// <summary>Returns <see langword="true"/> when <paramref name="prompt"/> looks like a short arithmetic question that <see cref="TryEvaluate"/> can handle deterministically without an online provider.</summary>
    public static bool LooksLikeSimpleArithmeticQuestion(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > MaxPromptLength)
        {
            return false;
        }

        var t = prompt.Trim().ToLowerInvariant();
        if (Regex.IsMatch(t, @"\b(code|function|class|error|exception|stack|usb|tpm|battery|lag|windows\s+update)\b"))
        {
            return false;
        }

        var hasDigit = t.Any(char.IsDigit);
        var hasWordNum = Regex.IsMatch(t,
            @"\b(zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety|hundred)\b");
        var hasOpWord = Regex.IsMatch(t,
            @"\b(times|multiplied\s+by|plus|minus|divided\s+by|percent\s+of|percentage\s+of)\b",
            RegexOptions.IgnoreCase);
        var hasSymOp = t.Contains('*') || t.Contains('×') || t.Contains('÷') ||
                       (t.Contains('+') && !t.Contains("c++", StringComparison.OrdinalIgnoreCase)) ||
                       (t.Contains('-') && Regex.IsMatch(t, @"\d\s*-\s*\d"));
        var hasSlashOp = Regex.IsMatch(t, @"\d\s*/\s*\d");
        var hasPercent = t.Contains('%') ||
                         Regex.IsMatch(t, @"\b\d+(?:\.\d+)?\s*(?:%|percent|percentage)\s+of\s+\d", RegexOptions.IgnoreCase);

        if (!hasDigit && !hasWordNum)
        {
            return false;
        }

        if (!(hasOpWord || hasSymOp || hasSlashOp || hasPercent || Regex.IsMatch(t, @"\b(what|whats|calculate|how\s+much|math)\b.*\d")))
        {
            return false;
        }

        return true;
    }

    /// <summary>Attempts to evaluate <paramref name="prompt"/> as a simple arithmetic expression. Returns <see langword="true"/> on success, setting <paramref name="answer"/> to the formatted result and <paramref name="normalizedExpression"/> to the normalised numeric expression used for computation.</summary>
    public static bool TryEvaluate(string? prompt, out string answer, out string? normalizedExpression)
    {
        answer = string.Empty;
        normalizedExpression = null;
        if (!LooksLikeSimpleArithmeticQuestion(prompt))
        {
            return false;
        }

        try
        {
            var work = prompt!.Trim().ToLowerInvariant();
            work = Regex.Replace(work, @"[""'`]", "");
            work = ExpandPercentOfPhrases(work);
            work = NormalizeOperatorWords(work);
            work = StripQuestionLead(work);
            work = ExpandEnglishNumberWords(work);
            work = work.Replace('×', '*').Replace('÷', '/').Replace(" ", string.Empty);

            if (!Regex.IsMatch(work, @"^[\d\.\+\-\*/\(\)]+$") ||
                !Regex.IsMatch(work, @"\d") ||
                !Regex.IsMatch(work, @"[\+\-\*/]"))
            {
                return false;
            }

            var forCompute = work.Replace("%", "/100", StringComparison.Ordinal);
            using var table = new DataTable();
            var raw = table.Compute(forCompute, string.Empty);
            var numeric = Convert.ToDecimal(raw, CultureInfo.InvariantCulture);
            normalizedExpression = work;
            var pretty = numeric == decimal.Truncate(numeric)
                ? decimal.Truncate(numeric).ToString(CultureInfo.InvariantCulture)
                : numeric.ToString("0.########", CultureInfo.InvariantCulture);

            if (prompt.Contains("times", StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(prompt, @"\b(ten|one|two|three|four|five|six|seven|eight|nine|\d+)\s+times\s+(ten|one|two|three|four|five|six|seven|eight|nine|\d+)", RegexOptions.IgnoreCase))
            {
                var m = Regex.Match(prompt,
                    @"\b(?<a>ten|one|two|three|four|five|six|seven|eight|nine|\d+)\s+times\s+(?<b>ten|one|two|three|four|five|six|seven|eight|nine|\d+)",
                    RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    answer = $"{FormatWordOrDigit(m.Groups["a"].Value)} × {FormatWordOrDigit(m.Groups["b"].Value)} = {pretty}.";
                    return true;
                }
            }

            answer = $"{pretty}.";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatWordOrDigit(string token)
    {
        var t = token.Trim();
        if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d))
        {
            return d.ToString(CultureInfo.InvariantCulture);
        }

        return char.ToUpperInvariant(t[0]) + t[1..];
    }

    private static string ExpandPercentOfPhrases(string t)
    {
        t = Regex.Replace(t, @"(\d+(?:\.\d+)?)\s*%\s*of\s*(\d+(?:\.\d+)?)", "($1/100*$2)", RegexOptions.IgnoreCase);
        return Regex.Replace(t, @"(\d+(?:\.\d+)?)\s+percent\s+of\s+(\d+(?:\.\d+)?)", "($1/100*$2)", RegexOptions.IgnoreCase);
    }

    private static string NormalizeOperatorWords(string t)
    {
        t = Regex.Replace(t, @"\b(times|multiplied\s+by)\b", " * ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\b(plus|and\s+then\s+add)\b", " + ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\bminus\b", " - ", RegexOptions.IgnoreCase);
        return Regex.Replace(t, @"\bdivided\s+by\b", " / ", RegexOptions.IgnoreCase);
    }

    private static string StripQuestionLead(string t)
    {
        return Regex.Replace(t, @"^\s*(what\'?s|whats|what\s+is|calculate|how\s+much\s+is|how\s+much\s+are|math:|please)\b[\s,:\-]*",
                "",
                RegexOptions.IgnoreCase)
            .Trim();
    }

    private static string ExpandEnglishNumberWords(string t)
    {
        var ones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["zero"] = "0",
            ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4", ["five"] = "5",
            ["six"] = "6", ["seven"] = "7", ["eight"] = "8", ["nine"] = "9"
        };
        var teens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ten"] = "10", ["eleven"] = "11", ["twelve"] = "12", ["thirteen"] = "13", ["fourteen"] = "14",
            ["fifteen"] = "15", ["sixteen"] = "16", ["seventeen"] = "17", ["eighteen"] = "18", ["nineteen"] = "19"
        };
        var tens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["twenty"] = "20", ["thirty"] = "30", ["forty"] = "40", ["fifty"] = "50",
            ["sixty"] = "60", ["seventy"] = "70", ["eighty"] = "80", ["ninety"] = "90"
        };

        t = Regex.Replace(t,
            @"\b(twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety)[\s\-](one|two|three|four|five|six|seven|eight|nine)\b",
            m =>
            {
                var tv = int.Parse(tens[m.Groups[1].Value], CultureInfo.InvariantCulture);
                var ov = int.Parse(ones[m.Groups[2].Value], CultureInfo.InvariantCulture);
                return (tv + ov).ToString(CultureInfo.InvariantCulture);
            },
            RegexOptions.IgnoreCase);

        foreach (var kv in tens)
        {
            t = Regex.Replace(t, $@"\b{Regex.Escape(kv.Key)}\b", kv.Value, RegexOptions.IgnoreCase);
        }

        foreach (var kv in teens)
        {
            t = Regex.Replace(t, $@"\b{Regex.Escape(kv.Key)}\b", kv.Value, RegexOptions.IgnoreCase);
        }

        foreach (var kv in ones)
        {
            t = Regex.Replace(t, $@"\b{Regex.Escape(kv.Key)}\b", kv.Value, RegexOptions.IgnoreCase);
        }

        return t;
    }
}
