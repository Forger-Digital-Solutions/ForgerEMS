using System.Text;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace VentoyToolkitSetup.Wpf.Services;

public enum KyraSlashCommandCategory
{
    Device,
    UsbToolkit,
    Resale,
    Code,
    LiveTools,
    Memory,
    App
}

public sealed class KyraSlashCommand
{
    public required string Name { get; init; }

    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();

    public required string Description { get; init; }

    public string Usage { get; init; } = string.Empty;

    public KyraSlashCommandCategory Category { get; init; }

    public KyraIntent HandlerIntent { get; init; } = KyraIntent.Unknown;
}

public sealed class KyraSlashCommandParseResult
{
    public bool IsSlashCommand { get; init; }

    public string RawInput { get; init; } = string.Empty;

    public string CommandToken { get; init; } = string.Empty;

    public string Arguments { get; init; } = string.Empty;

    public KyraSlashCommand? MatchedCommand { get; init; }

    public string? ErrorMessage { get; init; }

    public IReadOnlyList<string> SuggestedCommands { get; init; } = Array.Empty<string>();
}

public sealed class KyraSlashHostSnapshot
{
    public string LogsRoot { get; init; } = string.Empty;

    public string RuntimeRoot { get; init; } = string.Empty;

    public bool ApiFirstRouting { get; init; }

    public bool OfflineFallbackEnabled { get; init; }

    public string ModeDisplayName { get; init; } = string.Empty;

    public string ActiveProviderSummary { get; init; } = string.Empty;

    public string ToolStatusSummary { get; init; } = string.Empty;

    public bool MemoryEnabled { get; init; }

    public string UsbSummaryLine { get; init; } = string.Empty;

    public string ToolkitSummaryLine { get; init; } = string.Empty;

    public string LatestWarningSnippet { get; init; } = string.Empty;

    public SystemProfile? SystemProfile { get; init; }

    public SystemHealthEvaluation? Health { get; init; }

    public Action? ClearChatHistory { get; init; }

    public Action? ClearKyraMemoryConfirmed { get; init; }

    public Action? ExportKyraMemory { get; init; }

    public Action<bool>? SetKyraMemoryEnabled { get; init; }

    public Func<string>? BuildSanitizedMemoryPreview { get; init; }

    public bool VerboseLiveLogs { get; init; }

    public bool HasSystemIntelligenceScan { get; init; }

    public bool HasToolkitHealthReport { get; init; }

    public CopilotSettings ToolSettings { get; init; } = new();

    public Action? OpenLogsFolder { get; init; }

    public Action? NavigateToSettingsTab { get; init; }

    public Action? NavigateToSystemIntelligenceTab { get; init; }
}

public sealed class KyraSlashHandleResult
{
    public bool HandledWithoutLlm { get; init; }

    public string? ResponseText { get; init; }

    public string? ForwardPrompt { get; init; }

    public IReadOnlyList<KyraActionSuggestion> Actions { get; init; } = Array.Empty<KyraActionSuggestion>();

    public string SourceLabel { get; init; } = "Kyra · slash command";

    public CopilotResponse? ToCopilotResponse() =>
        !HandledWithoutLlm || string.IsNullOrEmpty(ResponseText)
            ? null
            : new CopilotResponse
            {
                Text = ResponseText!,
                UsedOnlineData = false,
                ProviderType = CopilotProviderType.LocalOffline,
                OnlineStatus = "Local command",
                SourceLabel = SourceLabel,
                ActionSuggestions = Actions,
                ProviderNotes = []
            };
}

public static class KyraSlashCommandRegistry
{
    public static IReadOnlyList<KyraSlashCommand> All { get; } =
    [
        new KyraSlashCommand
        {
            Name = "help",
            Aliases = ["h", "?"],
            Description = "List Kyra slash commands",
            Usage = "/help",
            Category = KyraSlashCommandCategory.App
        },
        new KyraSlashCommand
        {
            Name = "scan",
            Description = "System Intelligence scan reminder",
            Usage = "/scan",
            Category = KyraSlashCommandCategory.Device,
            HandlerIntent = KyraIntent.SystemHealthSummary
        },
        new KyraSlashCommand
        {
            Name = "diagnose",
            Aliases = ["diag"],
            Description = "Troubleshoot this PC using latest scan",
            Usage = "/diagnose [lag|battery|storage|wifi|gpu|boot|...]",
            Category = KyraSlashCommandCategory.Device,
            HandlerIntent = KyraIntent.PerformanceLag
        },
        new KyraSlashCommand
        {
            Name = "usb",
            Description = "USB / Ventoy readiness",
            Usage = "/usb",
            Category = KyraSlashCommandCategory.UsbToolkit,
            HandlerIntent = KyraIntent.USBBuilderHelp
        },
        new KyraSlashCommand
        {
            Name = "toolkit",
            Aliases = ["tk"],
            Description = "Toolkit Manager health summary",
            Usage = "/toolkit",
            Category = KyraSlashCommandCategory.UsbToolkit,
            HandlerIntent = KyraIntent.ToolkitManagerHelp
        },
        new KyraSlashCommand
        {
            Name = "warning",
            Aliases = ["warn"],
            Description = "Explain latest warning",
            Usage = "/warning",
            Category = KyraSlashCommandCategory.Device,
            HandlerIntent = KyraIntent.ForgerEMSQuestion
        },
        new KyraSlashCommand
        {
            Name = "resale",
            Aliases = ["flip", "worth"],
            Description = "Resale / flip advisor",
            Usage = "/resale",
            Category = KyraSlashCommandCategory.Resale,
            HandlerIntent = KyraIntent.ResaleValue
        },
        new KyraSlashCommand
        {
            Name = "listing",
            Description = "Draft safe listing copy",
            Usage = "/listing [facebook|offerup|ebay]",
            Category = KyraSlashCommandCategory.Resale,
            HandlerIntent = KyraIntent.ResaleValue
        },
        new KyraSlashCommand
        {
            Name = "os",
            Description = "OS recommendation for this machine",
            Usage = "/os",
            Category = KyraSlashCommandCategory.Device,
            HandlerIntent = KyraIntent.OSRecommendation
        },
        new KyraSlashCommand
        {
            Name = "fixcode",
            Aliases = ["code"],
            Description = "Code / config assist",
            Usage = "/fixcode",
            Category = KyraSlashCommandCategory.Code,
            HandlerIntent = KyraIntent.CodeAssist
        },
        new KyraSlashCommand
        {
            Name = "weather",
            Description = "Weather (if configured)",
            Usage = "/weather [location]",
            Category = KyraSlashCommandCategory.LiveTools,
            HandlerIntent = KyraIntent.Weather
        },
        new KyraSlashCommand
        {
            Name = "news",
            Description = "News (if configured)",
            Usage = "/news [topic]",
            Category = KyraSlashCommandCategory.LiveTools,
            HandlerIntent = KyraIntent.News
        },
        new KyraSlashCommand
        {
            Name = "stocks",
            Aliases = ["stock"],
            Description = "Stock quote (if configured)",
            Usage = "/stocks [symbol]",
            Category = KyraSlashCommandCategory.LiveTools,
            HandlerIntent = KyraIntent.StockPrice
        },
        new KyraSlashCommand
        {
            Name = "crypto",
            Description = "Crypto quote (if configured)",
            Usage = "/crypto [symbol]",
            Category = KyraSlashCommandCategory.LiveTools,
            HandlerIntent = KyraIntent.CryptoPrice
        },
        new KyraSlashCommand
        {
            Name = "sports",
            Description = "Sports (if configured)",
            Usage = "/sports [team|league]",
            Category = KyraSlashCommandCategory.LiveTools,
            HandlerIntent = KyraIntent.Sports
        },
        new KyraSlashCommand
        {
            Name = "memory",
            Description = "Local Kyra memory",
            Usage = "/memory [view|clear|export|on|off]",
            Category = KyraSlashCommandCategory.Memory
        },
        new KyraSlashCommand
        {
            Name = "clear",
            Description = "Clear Kyra chat history",
            Usage = "/clear",
            Category = KyraSlashCommandCategory.App
        },
        new KyraSlashCommand
        {
            Name = "logs",
            Description = "Log files help",
            Usage = "/logs",
            Category = KyraSlashCommandCategory.App
        },
        new KyraSlashCommand
        {
            Name = "settings",
            Description = "Settings location help",
            Usage = "/settings",
            Category = KyraSlashCommandCategory.App
        },
        new KyraSlashCommand
        {
            Name = "provider",
            Aliases = ["providers"],
            Description = "Provider & tool status",
            Usage = "/provider",
            Category = KyraSlashCommandCategory.App
        }
    ];

    public static KyraSlashCommand? Match(string token)
    {
        var t = token.Trim().TrimStart('/').ToLowerInvariant();
        if (string.IsNullOrEmpty(t))
        {
            return null;
        }

        foreach (var c in All)
        {
            if (c.Name.Equals(t, StringComparison.OrdinalIgnoreCase))
            {
                return c;
            }

            if (c.Aliases.Any(a => a.Equals(t, StringComparison.OrdinalIgnoreCase)))
            {
                return c;
            }
        }

        return null;
    }
}

public static class KyraSlashCommandParser
{
    public static KyraSlashCommandParseResult Parse(string input)
    {
        var raw = input.Trim();
        if (string.IsNullOrEmpty(raw) || raw[0] != '/')
        {
            return new KyraSlashCommandParseResult { RawInput = raw, IsSlashCommand = false };
        }

        var body = raw[1..].Trim();
        if (string.IsNullOrEmpty(body))
        {
            return new KyraSlashCommandParseResult
            {
                IsSlashCommand = true,
                RawInput = raw,
                ErrorMessage =
                    "You typed “/” alone. Try `/help` for commands, or start typing `/d` for `/diagnose`, `/re` for `/resale`, etc.",
                SuggestedCommands = KyraSlashCommandRegistry.All.Select(c => "/" + c.Name).Take(16).ToArray()
            };
        }

        string cmdToken;
        string args;
        var space = body.IndexOf(' ');
        if (space < 0)
        {
            cmdToken = body;
            args = string.Empty;
        }
        else
        {
            cmdToken = body[..space].Trim();
            args = body[(space + 1)..].Trim();
        }

        var matched = KyraSlashCommandRegistry.Match(cmdToken);
        if (matched is null)
        {
            var suggestions = KyraSlashCommandRegistry.All
                .Where(c => c.Name.StartsWith(cmdToken, StringComparison.OrdinalIgnoreCase) ||
                            c.Aliases.Any(a => a.StartsWith(cmdToken, StringComparison.OrdinalIgnoreCase)))
                .Select(c => "/" + c.Name)
                .Take(8)
                .ToArray();

            if (suggestions.Length == 0)
            {
                suggestions = KyraSlashCommandParser.FindClosestCommands(cmdToken, 6)
                    .Select(c => "/" + c)
                    .ToArray();
            }

            if (suggestions.Length == 0)
            {
                suggestions = ["/help", "/warning", "/diagnose", "/provider"];
            }

            var didYouMean = suggestions.Length > 0
                ? $" Or try: {suggestions[0]}"
                : string.Empty;
            var warnHint = cmdToken.Contains("warn", StringComparison.OrdinalIgnoreCase) ? " Did you mean `/warning`?" : string.Empty;

            return new KyraSlashCommandParseResult
            {
                IsSlashCommand = true,
                RawInput = raw,
                CommandToken = cmdToken,
                Arguments = args,
                ErrorMessage =
                    $"I don’t know `/{cmdToken}` yet. Try `/help` for the full list.{didYouMean}{warnHint}",
                SuggestedCommands = suggestions
            };
        }

        return new KyraSlashCommandParseResult
        {
            IsSlashCommand = true,
            RawInput = raw,
            CommandToken = cmdToken,
            Arguments = args,
            MatchedCommand = matched
        };
    }

    public static string BuildHelpText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("**Kyra commands** — technician-style shortcuts. Type at the start of a message.");
        sb.AppendLine();
        AppendSection(sb, "Device", KyraSlashCommandCategory.Device);
        AppendSection(sb, "USB & Toolkit", KyraSlashCommandCategory.UsbToolkit);
        AppendSection(sb, "Resale", KyraSlashCommandCategory.Resale);
        AppendSection(sb, "Code", KyraSlashCommandCategory.Code);
        AppendSection(sb, "Live tools", KyraSlashCommandCategory.LiveTools);
        AppendSection(sb, "Memory", KyraSlashCommandCategory.Memory);
        AppendSection(sb, "App", KyraSlashCommandCategory.App);
        sb.AppendLine("**Examples**");
        sb.AppendLine("- `/diagnose lag`");
        sb.AppendLine("- `/listing facebook`");
        sb.AppendLine("- `/weather 11710`");
        sb.AppendLine("- `/crypto BTC` · `/stocks NVDA`");
        sb.AppendLine("- `/memory view` · `/provider`");
        sb.AppendLine();
        sb.AppendLine("Tip: type `/` then a few letters — the chat box suggests matches (arrows, Tab, Enter).");
        return sb.ToString().TrimEnd();
    }

    private static void AppendSection(StringBuilder sb, string title, KyraSlashCommandCategory category)
    {
        sb.AppendLine("**" + title + "**");
        foreach (var c in KyraSlashCommandRegistry.All.Where(x => x.Category == category).OrderBy(x => x.Name))
        {
            var aliases = c.Aliases.Count > 0 ? $" (aliases: {string.Join(", ", c.Aliases.Select(a => "/" + a))})" : string.Empty;
            sb.AppendLine($"- `/{c.Name}` — {c.Description}{aliases}");
            if (!string.IsNullOrWhiteSpace(c.Usage) && !c.Usage.Equals("/" + c.Name, StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"  `{c.Usage}`");
            }
        }

        sb.AppendLine();
    }

    /// <summary>Names only (no slash), closest edit distance.</summary>
    public static IReadOnlyList<string> FindClosestCommands(string token, int max)
    {
        var t = token.Trim().TrimStart('/').ToLowerInvariant();
        if (string.IsNullOrEmpty(t))
        {
            return [];
        }

        var scored = new List<(string Name, int Dist)>();
        foreach (var c in KyraSlashCommandRegistry.All)
        {
            var d = Math.Min(Levenshtein(t, c.Name.ToLowerInvariant()),
                c.Aliases.Select(a => Levenshtein(t, a.ToLowerInvariant())).DefaultIfEmpty(int.MaxValue).Min());
            if (d <= 3)
            {
                scored.Add((c.Name, d));
            }
        }

        return scored
            .OrderBy(x => x.Dist)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToArray();
    }

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }
}

public static class KyraSlashCommandRouter
{
    public static KyraSlashHandleResult Handle(KyraSlashCommandParseResult parse, KyraSlashHostSnapshot host)
    {
        if (!parse.IsSlashCommand || parse.MatchedCommand is null)
        {
            if (!string.IsNullOrEmpty(parse.ErrorMessage))
            {
                var hint = parse.SuggestedCommands.Count > 0
                    ? "\n\nTry: " + string.Join(", ", parse.SuggestedCommands)
                    : string.Empty;
                return new KyraSlashHandleResult
                {
                    HandledWithoutLlm = true,
                    ResponseText = parse.ErrorMessage + hint,
                    SourceLabel = "Kyra · command help"
                };
            }

            return new KyraSlashHandleResult();
        }

        var cmd = parse.MatchedCommand.Name;
        var args = parse.Arguments;

        return cmd switch
        {
            "help" => new KyraSlashHandleResult
            {
                HandledWithoutLlm = true,
                ResponseText = KyraSlashCommandParser.BuildHelpText(),
                SourceLabel = "Kyra · /help"
            },
            "scan" => ScanCommand(host, args),
            "diagnose" => Forward(DiagnosePrompt(args)),
            "usb" => Forward(
                "Using ForgerEMS context: explain current USB target readiness for a Ventoy repair stick, what to check in Disk Management, and whether the selected drive looks right. " +
                (string.IsNullOrWhiteSpace(host.UsbSummaryLine) ? "USB summary not loaded in context." : "USB context: " + host.UsbSummaryLine)),
            "toolkit" => Forward(
                "Summarize Toolkit Manager health in plain English from context: installed vs missing vs manual vs placeholders, and what the technician should do next. " +
                (string.IsNullOrWhiteSpace(host.ToolkitSummaryLine) ? "" : "Toolkit context: " + host.ToolkitSummaryLine)),
            "warning" => Forward(WarningPrompt(host)),
            "resale" => Forward(
                "Act as Kyra resale advisor: use System Intelligence + pricing engine context. Give local estimate range if available, confidence, what helps/hurts value, upgrade-before-sell priorities, and say clearly if live marketplace comps are not configured."),
            "listing" => ListingInline(host, args),
            "os" => Forward(
                "Recommend the best OS options for this exact machine using TPM, Secure Boot, CPU generation, RAM, and storage from System Intelligence. Keep it practical."),
            "fixcode" => Forward(
                string.IsNullOrWhiteSpace(args)
                    ? "Kyra code assist: the user invoked /fixcode with no pasted snippet. Ask them to paste code in the next message and identify language (PowerShell, C#, XAML, JSON, YAML, etc.). Do not execute anything."
                    : $"Kyra code assist. User pasted or attached this after /fixcode — analyze what’s wrong, propose a fixed snippet, explain why, and warn if destructive:\n{args}"),
            "weather" => Forward(string.IsNullOrWhiteSpace(args) ? "What is the weather? (user ran /weather without a location — ask for city or ZIP.)" : $"What is the weather for {args}?"),
            "news" => Forward(string.IsNullOrWhiteSpace(args) ? "Latest news headlines (user ran /news without topic — ask what topic.)" : $"Latest news about {args}"),
            "stocks" => Forward(string.IsNullOrWhiteSpace(args)
                ? "Stock price (user ran /stocks without symbol — ask for ticker.)"
                : $"Stock price for ticker {args}. Informational only, not financial advice. If live tool not configured, say so."),
            "crypto" => Forward(string.IsNullOrWhiteSpace(args)
                ? "Crypto price (user ran /crypto without symbol — ask which coin.)"
                : $"Price for {args}. Informational only, not financial advice. If live tool not configured, say so."),
            "sports" => Forward(string.IsNullOrWhiteSpace(args)
                ? "Sports scores (user ran /sports without team — ask which team or league.)"
                : $"Sports update for {args}."),
            "memory" => MemoryCommand(host, args),
            "clear" => ClearChat(host),
            "logs" => LogsCommand(host, args),
            "settings" => SettingsCommand(host, args),
            "provider" => ProviderStatus(host),
            _ => new KyraSlashHandleResult()
        };
    }

    private static KyraSlashHandleResult ScanCommand(KyraSlashHostSnapshot host, string args)
    {
        var a = args.Trim().ToLowerInvariant();
        if (a is "open")
        {
            host.NavigateToSystemIntelligenceTab?.Invoke();
            return new KyraSlashHandleResult
            {
                HandledWithoutLlm = true,
                ResponseText =
                    "Opened the **System Intelligence** tab. Tap **Scan** when you’re ready — I won’t auto-run it from chat.",
                SourceLabel = "Kyra · /scan open"
            };
        }

        return new KyraSlashHandleResult
        {
            HandledWithoutLlm = true,
            ResponseText =
                "I won’t auto-run a scan from chat (safety). Open **System Intelligence** and run **Scan**, then try `/diagnose`.\n\n" +
                "Tip: send **`/scan open`** to jump to that tab.",
            Actions =
            [
                new KyraActionSuggestion
                {
                    Title = "Open System Intelligence",
                    Description = "Jump to the tab, then run Scan yourself.",
                    Category = "Device",
                    SuggestedPrompt = "/scan open",
                    RelatedTab = "System Intelligence"
                },
                new KyraActionSuggestion
                {
                    Title = "Diagnose after scan",
                    Description = "Use fresh report context.",
                    Category = "Device",
                    SuggestedPrompt = "/diagnose lag"
                }
            ],
            SourceLabel = "Kyra · /scan"
        };
    }

    private static KyraSlashHandleResult Forward(string prompt) =>
        new() { ForwardPrompt = prompt };

    private static string DiagnosePrompt(string issue)
    {
        var i = string.IsNullOrWhiteSpace(issue) ? "general performance" : issue.Trim();
        return
            $"Diagnose this PC focusing on: {i}. Use latest System Intelligence context. " +
            "Answer with: Short answer → What I noticed → Most likely cause → What to do next → Caution if any.";
    }

    private static string WarningPrompt(KyraSlashHostSnapshot host)
    {
        if (string.IsNullOrWhiteSpace(host.LatestWarningSnippet))
        {
            return "No obvious warning line was captured from recent logs/diagnostics in context. Ask the user to reproduce the warning or run System Intelligence / Toolkit scan, then try /warning again.";
        }

        return
            "Explain this warning in plain English and what to do next. Context warning line: " +
            host.LatestWarningSnippet;
    }

    private static KyraSlashHandleResult ListingInline(KyraSlashHostSnapshot host, string args)
    {
        var ch = args.ToLowerInvariant().Trim() switch
        {
            "ebay" or "e" => KyraListingDraft.ListingChannel.Ebay,
            "offerup" or "ou" => KyraListingDraft.ListingChannel.OfferUp,
            _ => KyraListingDraft.ListingChannel.Facebook
        };

        var draft = KyraListingDraft.Build(host.SystemProfile, ch, host.Health);
        return new KyraSlashHandleResult
        {
            HandledWithoutLlm = true,
            ResponseText =
                "Here’s a **safe draft** from sanitized specs only (no serials, keys, or paths). " +
                "Review before posting — I’m not a marketplace lawyer.\n\n" + draft,
            Actions =
            [
                new KyraActionSuggestion
                {
                    Title = "Tune listing with /resale",
                    Description = "Get upgrade and pricing context next.",
                    Category = "Resale",
                    SuggestedPrompt = "/resale"
                }
            ],
            SourceLabel = "Kyra · /listing"
        };
    }

    private static KyraSlashHandleResult MemoryCommand(KyraSlashHostSnapshot host, string args)
    {
        var a = args.ToLowerInvariant().Trim();
        if (string.IsNullOrEmpty(a) || a is "status" or "info")
        {
            return new KyraSlashHandleResult
            {
                HandledWithoutLlm = true,
                ResponseText =
                    "Kyra memory is **local-only** on this PC. It stores safe preferences — never API keys, passwords, serials, or recovery keys.\n\n" +
                    $"Status: **{(host.MemoryEnabled ? "enabled" : "disabled")}**\n\n" +
                    "Commands:\n- `/memory view` — sanitized JSON preview\n" +
                    "- `/memory export` — save sanitized export\n" +
                    "- `/memory clear confirm` — wipe stored memory\n" +
                    "- `/memory on` / `/memory off` — toggle",
                SourceLabel = "Kyra · /memory"
            };
        }

        if (a is "on")
        {
            host.SetKyraMemoryEnabled?.Invoke(true);
            return new KyraSlashHandleResult
            {
                HandledWithoutLlm = true,
                ResponseText =
                    "Memory **on**. I’ll only keep non-sensitive preferences locally — nothing is synced to the cloud. You can clear anytime with `/memory clear confirm`.",
                SourceLabel = "Kyra · memory"
            };
        }

        if (a is "off")
        {
            host.SetKyraMemoryEnabled?.Invoke(false);
            return new KyraSlashHandleResult
            {
                HandledWithoutLlm = true,
                ResponseText = "Memory **off**. I won’t add new preference hints until you turn it back on.",
                SourceLabel = "Kyra · memory"
            };
        }

        if (a.StartsWith("view", StringComparison.Ordinal))
        {
            var preview = host.BuildSanitizedMemoryPreview?.Invoke() ?? "{}";
            return new KyraSlashHandleResult
            {
                HandledWithoutLlm = true,
                ResponseText = "Sanitized local memory snapshot:\n\n" + preview,
                SourceLabel = "Kyra · memory view"
            };
        }

        if (a.StartsWith("export", StringComparison.Ordinal))
        {
            host.ExportKyraMemory?.Invoke();
            return new KyraSlashHandleResult
            {
                HandledWithoutLlm = true,
                ResponseText = "If a save dialog opened, pick where to store the **sanitized** export. Keys and secrets are never written.",
                SourceLabel = "Kyra · memory export"
            };
        }

        if (a.StartsWith("clear", StringComparison.Ordinal))
        {
            if (!a.Contains("confirm", StringComparison.Ordinal))
            {
                return new KyraSlashHandleResult
                {
                    HandledWithoutLlm = true,
                    ResponseText =
                        "I can clear Kyra’s **local** memory on this PC (saved preferences only — not your chat). " +
                        "Type **`/memory clear confirm`** to continue.",
                    SourceLabel = "Kyra · memory"
                };
            }

            host.ClearKyraMemoryConfirmed?.Invoke();
            return new KyraSlashHandleResult
            {
                HandledWithoutLlm = true,
                ResponseText = "Local Kyra memory cleared. You can rebuild safe preferences anytime with `/memory on`.",
                SourceLabel = "Kyra · memory cleared"
            };
        }

        return new KyraSlashHandleResult
        {
            HandledWithoutLlm = true,
            ResponseText = "Unknown /memory subcommand. Try `/memory` for usage.",
            SourceLabel = "Kyra · memory"
        };
    }

    private static KyraSlashHandleResult ClearChat(KyraSlashHostSnapshot host)
    {
        host.ClearChatHistory?.Invoke();
        return new KyraSlashHandleResult
        {
            HandledWithoutLlm = true,
            ResponseText =
                "Chat cleared. **Saved Kyra disk memory** (if any) was not wiped — only `/memory clear confirm` does that.",
            SourceLabel = "Kyra · /clear"
        };
    }

    private static KyraSlashHandleResult LogsCommand(KyraSlashHostSnapshot host, string args)
    {
        var a = args.Trim().ToLowerInvariant();
        if (a is "open")
        {
            host.OpenLogsFolder?.Invoke();
            return new KyraSlashHandleResult
            {
                HandledWithoutLlm = true,
                ResponseText =
                    "Opening your **logs** folder. Redact secrets before you attach files or paste log text.",
                SourceLabel = "Kyra · /logs open"
            };
        }

        var pathLine = string.IsNullOrWhiteSpace(host.LogsRoot)
            ? "Logs live under your ForgerEMS runtime directory (see **Settings**)."
            : host.LogsRoot;
        return new KyraSlashHandleResult
        {
            HandledWithoutLlm = true,
            ResponseText =
                "**Logs**\n\n" +
                $"{pathLine}\n\n" +
                "Use the footer **Copy logs** when reporting issues. Never paste API keys or passwords.\n\n" +
                "Send **`/logs open`** when you want Kyra to open that folder (explicit action).",
            Actions =
            [
                new KyraActionSuggestion
                {
                    Title = "Open logs folder",
                    Description = "Send /logs open (Kyra will not auto-open without it).",
                    Category = "App",
                    SuggestedPrompt = "/logs open",
                    RelatedTab = "Settings"
                }
            ],
            SourceLabel = "Kyra · /logs"
        };
    }

    private static KyraSlashHandleResult SettingsCommand(KyraSlashHostSnapshot host, string args)
    {
        var a = args.Trim().ToLowerInvariant();
        if (a is "open")
        {
            host.NavigateToSettingsTab?.Invoke();
            return new KyraSlashHandleResult
            {
                HandledWithoutLlm = true,
                ResponseText = "Switching to the **Settings** tab.",
                SourceLabel = "Kyra · /settings open"
            };
        }

        return new KyraSlashHandleResult
        {
            HandledWithoutLlm = true,
            ResponseText =
                "**Settings**\n\n" +
                $"- Runtime root: `{host.RuntimeRoot}`\n" +
                "- Kyra providers & privacy: **Kyra → Advanced…**\n" +
                "- Beta verbose logs: **Settings** tab\n\n" +
                "Send **`/settings open`** to jump to Settings in the main window.",
            Actions =
            [
                new KyraActionSuggestion
                {
                    Title = "Open Settings tab",
                    Description = "Send /settings open.",
                    Category = "App",
                    SuggestedPrompt = "/settings open"
                }
            ],
            SourceLabel = "Kyra · /settings"
        };
    }

    private static KyraSlashHandleResult ProviderStatus(KyraSlashHostSnapshot host)
    {
        var facts = new KyraToolHostFacts
        {
            HasSystemIntelligenceScan = host.HasSystemIntelligenceScan,
            HasToolkitHealthReport = host.HasToolkitHealthReport
        };
        var reg = new KyraToolRegistry();
        var toolBlock = reg.BuildProviderToolDetailText(host.ToolSettings, facts, host.VerboseLiveLogs);
        return new()
        {
            HandledWithoutLlm = true,
            ResponseText =
                "**Provider & tools**\n\n" +
                $"- Mode: {host.ModeDisplayName}\n" +
                $"- API-first routing: {(host.ApiFirstRouting ? "on" : "off")}\n" +
                $"- Offline fallback: {(host.OfflineFallbackEnabled ? "enabled" : "disabled")}\n" +
                $"- Kyra memory (disk): {(host.MemoryEnabled ? "on" : "off")}\n" +
                $"- Verbose beta logs: {(host.VerboseLiveLogs ? "on" : "off")}\n\n" +
                host.ActiveProviderSummary + "\n\n" +
                "**Tool status**\n" + toolBlock,
            SourceLabel = "Kyra · /provider"
        };
    }
}
