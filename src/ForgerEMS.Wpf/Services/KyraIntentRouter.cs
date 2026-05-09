#pragma warning disable CA1822 // DI-related code; instance methods called via interface references
// KYRA_CORE_CANDIDATE: routing algorithm is generic.
// FORGEREMS_KYRA_ADAPTER: keyword lists reference ForgerEMS product terms (USB, Ventoy, Toolkit, scan); extract to IIntentKeywordSource.
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace VentoyToolkitSetup.Wpf.Services;

public static class KyraIntentRouter
{
    public static KyraIntent DetectIntent(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return KyraIntent.Unknown;
        }

        if (KyraPromptIsolation.LooksLikeKyraWindowsEnvConfigurationQuestion(prompt))
        {
            return KyraIntent.ForgerEMSQuestion;
        }

        var text = prompt.ToLowerInvariant();
        if (ContainsAny(
                text,
                "what device",
                "which device",
                "this device",
                "what computer",
                "which computer",
                "machine are we",
                "we working on",
                "device are we",
                "about this laptop",
                "about this pc",
                "specs of this",
                "specs for this"))
        {
            return KyraIntent.SystemHealthSummary;
        }

        if (KyraCodeSnippetDetector.LooksLikeCodeSnippet(prompt))
        {
            return KyraIntent.CodeAssist;
        }

        if (ContainsAny(text, "weather", "forecast", "humidity", "precipitation", "celsius", "fahrenheit") &&
            !ContainsAny(text, "ssd", "storage health", "forecast upgrade"))
        {
            return KyraIntent.Weather;
        }

        if (ContainsAny(text, "headline", "breaking news", "in the news", "news today", "current events") ||
            (text.Contains("news", StringComparison.OrdinalIgnoreCase) &&
             !ContainsAny(text, "usb", "ventoy", "toolkit", "driver")))
        {
            return KyraIntent.News;
        }

        if (ContainsAny(text, "bitcoin", "ethereum", "btc", "eth", "dogecoin", "solana", "crypto price", "altcoin"))
        {
            return KyraIntent.CryptoPrice;
        }

        if (ContainsAny(text, "stock", "ticker", "nasdaq", "nyse", "s&p", "share price", "equity") &&
            !ContainsAny(text, "usb stick", "thumb drive", "ventoy"))
        {
            return KyraIntent.StockPrice;
        }

        if (ContainsAny(text, "nfl", "nba", "mlb", "nhl", "soccer score", "super bowl", "world cup", "final score", "playoff"))
        {
            return KyraIntent.Sports;
        }

        if (ContainsAny(text, "today", "right now", "current", "latest", "newest", "live", "current price", "current version", "current drivers", "current driver", "current market") &&
            ContainsAny(
                text,
                "crypto",
                "bitcoin",
                "btc",
                "stock",
                "weather",
                "news",
                "sports",
                "score",
                "version",
                "driver",
                "drivers",
                "ventoy",
                "market",
                "price",
                "pricing",
                "cve",
                "security advisory",
                "windows issue",
                "windows update"))
        {
            return KyraIntent.LiveOnlineQuestion;
        }

        if (ContainsAny(text, "right now", "live price", "at this moment") &&
            ContainsAny(text, "price", "market", "exchange rate"))
        {
            return KyraIntent.LiveOnlineQuestion;
        }

        if (ContainsAny(text, "prime video", "netflix", "youtube", "browser", "edge", "chrome") &&
            ContainsAny(text, "lag", "slow", "freezing", "freeze", "stutter", "hang", "crash"))
        {
            return KyraIntent.AppFreezing;
        }

        if (ContainsAny(
                text,
                "before beta",
                "human testing",
                "beta testing",
                "release checklist",
                "missing before",
                "ready for beta",
                "beta readiness",
                "what should i test before beta"))
        {
            return KyraIntent.ForgerEMSQuestion;
        }

        if (ContainsAny(
                text,
                "smartscreen",
                "smart screen",
                "windows protected your pc",
                "unrecognized app",
                "publisher unknown",
                "defender smartscreen"))
        {
            return KyraIntent.ForgerEMSQuestion;
        }

        if (ContainsAny(
                text,
                "map usb",
                "usb mapping",
                "map port",
                "label port",
                "mapping session",
                "how do i map",
                "map my usb"))
        {
            return KyraIntent.USBBuilderHelp;
        }

        if (ContainsAny(text, "freezing", "freeze", "hang", "not responding", "crash", "stuck"))
        {
            return KyraIntent.AppFreezing;
        }

        if (ContainsAny(text, "slow boot", "boot slow", "startup slow", "takes forever to start", "start up slow", "login slow"))
        {
            return KyraIntent.SlowBoot;
        }

        if ((ContainsAny(text, "usb", "flash drive", "thumb drive", "stick") &&
             ContainsAny(text, "slow", "speed", "faster", "throughput", "transfer", "bottleneck")) ||
            ContainsAny(text, "best port", "which port", "usb-c port", "usb port"))
        {
            return KyraIntent.USBBuilderHelp;
        }

        if (ContainsAny(text, "what's wrong", "whats wrong", "something wrong", "unified diagnostics"))
        {
            return KyraIntent.SystemHealthSummary;
        }

        if (ContainsAny(text, "lag", "slow", "stutter", "sluggish", "choppy", "bottleneck"))
        {
            return KyraIntent.PerformanceLag;
        }

        if (ContainsAny(text, "usb builder", "ventoy", "usb", "flash drive", "drive not showing", "vtoyefi"))
        {
            return KyraIntent.USBBuilderHelp;
        }

        if (ContainsAny(text, "toolkit", "download", "missing tools", "tool missing", "iso", "rescuezilla", "clonezilla"))
        {
            return KyraIntent.ToolkitManagerHelp;
        }

        if (ContainsAny(text, "gpu", "graphics", "nvidia", "radeon", "intel uhd", "display driver", "vram"))
        {
            return KyraIntent.GPUQuestion;
        }

        if (ContainsAny(text, "driver", "bios", "chipset", "device manager", "missing driver"))
        {
            return KyraIntent.DriverIssue;
        }

        if (ContainsAny(text, "storage", "ssd", "nvme", "hard drive", "disk", "smart", "wear", "bad sectors"))
        {
            return KyraIntent.StorageIssue;
        }

        if (ContainsAny(text, "memory", "ram", "16gb", "32gb", "ddr4", "ddr5"))
        {
            return KyraIntent.MemoryIssue;
        }

        if (ContainsAny(text, "upgrade", "better", "improve", "faster", "what should i upgrade", "upgrade first"))
        {
            return KyraIntent.UpgradeAdvice;
        }

        if (ContainsAny(text, "today", "right now", "current", "latest", "newest", "live", "current price", "current version", "current drivers", "current driver", "current market") &&
            ContainsAny(
                text,
                "crypto",
                "bitcoin",
                "btc",
                "stock",
                "weather",
                "news",
                "sports",
                "score",
                "version",
                "driver",
                "drivers",
                "ventoy",
                "market",
                "price",
                "pricing",
                "cve",
                "security advisory",
                "windows issue",
                "windows update"))
        {
            return KyraIntent.LiveOnlineQuestion;
        }

        if (ContainsAny(text, "worth", "sell", "selling", "price", "value", "resale", "flip", "listing", "profit", "comps", "ebay"))
        {
            return KyraIntent.ResaleValue;
        }

        if (ContainsAny(text, "windows 11", "windows 10", "linux", "ubuntu", "mint", "xubuntu", "what os", "which os", "best os", "reinstall"))
        {
            return KyraIntent.OSRecommendation;
        }

        if (IsKyraInAppHelpQuestion(text))
        {
            return KyraIntent.ForgerEMSQuestion;
        }

        if (ContainsAny(
                text,
                "current law",
                "new law",
                "statute today",
                "legal update today",
                "regulation today",
                "what is the law today"))
        {
            return KyraIntent.LiveOnlineQuestion;
        }

        if (ContainsAny(text, "newest", "latest") &&
            ContainsAny(text, "release", "version") &&
            !ContainsAny(text, "forgerems", "forger ems"))
        {
            return KyraIntent.LiveOnlineQuestion;
        }

        if (LooksLikeCasualGreetingOrGeneralAssistantChat(text))
        {
            return KyraIntent.GeneralTechQuestion;
        }

        if (ContainsAny(text, "forgerems", "how do i use", "what does this app", "system intelligence", "settings tab"))
        {
            return KyraIntent.ForgerEMSQuestion;
        }

        if (ContainsAny(text, "scan", "system", "spec", "health", "diagnose this pc", "device report"))
        {
            return KyraIntent.SystemHealthSummary;
        }

        return text.Length < 4 ? KyraIntent.Unknown : KyraIntent.GeneralTechQuestion;
    }

    /// <summary>
    /// User question appears to be about the machine Kyra is running on (not a generic tech essay).
    /// </summary>
    public static bool PromptReferencesThisMachine(string textLower)
    {
        return textLower.Contains("this laptop", StringComparison.OrdinalIgnoreCase) ||
               textLower.Contains("this pc", StringComparison.OrdinalIgnoreCase) ||
               textLower.Contains("this computer", StringComparison.OrdinalIgnoreCase) ||
               textLower.Contains("this machine", StringComparison.OrdinalIgnoreCase) ||
               textLower.Contains("from this one", StringComparison.OrdinalIgnoreCase) ||
               textLower.Contains("my laptop", StringComparison.OrdinalIgnoreCase) ||
               textLower.Contains("on this machine", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// In-app Kyra configuration / behavior (stay local) — not casual "Hi Kyra" chat.
    /// </summary>
    private static bool IsKyraInAppHelpQuestion(string text)
    {
        if (!text.Contains("kyra", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ContainsAny(
                text,
                "why is kyra offline",
                "kyra offline",
                "configure kyra",
                "configuring kyra",
                "kyra settings",
                "kyra provider",
                "kyra providers",
                "kyra advanced",
                "enable kyra",
                "disable kyra",
                "turn on kyra",
                "turn off kyra",
                "kyra api",
                "refresh provider",
                "how do i use kyra",
                "how does kyra work in",
                "where is kyra",
                "kyra in forgerems",
                "forgerems kyra"))
        {
            return true;
        }

        if (text.Contains("kyra", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("session", StringComparison.OrdinalIgnoreCase) &&
            ContainsAny(text, "key", "api", "token"))
        {
            return true;
        }

        if (text.Contains("configure", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("kyra", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Greetings and assistant-style chat should route API-first (free pool / BYOK), not in-app help.
    /// </summary>
    private static bool LooksLikeCasualGreetingOrGeneralAssistantChat(string text)
    {
        var t = text.Trim();
        if (t.Length == 0)
        {
            return false;
        }

        if (Regex.IsMatch(t, @"^(hi|hey|hello|yo|hiya|howdy|greetings|sup)\b([\s,!.?]+(there|again|kyra|friend)){0,4}\s*[!.?]*$", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(t, @"^(good morning|good afternoon|good evening)\b([\s,!.?]+(there|kyra|friend)){0,4}\s*[!.?]*$", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (ContainsAny(
                text,
                "can you help me",
                "what can you do",
                "help me think",
                "explain this better",
                "write this cleaner",
                "rewrite this professionally",
                "rewrite professionally",
                "brainstorm",
                "make this listing sound better",
                "compare windows",
                "compare ubuntu",
                "windows vs",
                "ubuntu vs",
                "linux vs"))
        {
            return true;
        }

        return false;
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
