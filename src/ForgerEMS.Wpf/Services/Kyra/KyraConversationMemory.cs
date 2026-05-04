using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.Kyra;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class KyraConversationTurn
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public string UserMessage { get; init; } = string.Empty;

    public string KyraResponseSummary { get; init; } = string.Empty;

    public KyraIntent Intent { get; init; } = KyraIntent.Unknown;

    public string SystemSnapshot { get; init; } = string.Empty;

    public string UnresolvedIssue { get; init; } = string.Empty;

    public string LastRecommendation { get; init; } = string.Empty;

    public bool GaveDiagnosticBreakdown { get; init; }
}

public sealed class KyraConversationState
{
    public KyraIntent LastIntent { get; init; } = KyraIntent.Unknown;
    public string LastKyraSummary { get; init; } = string.Empty;
    public string UnresolvedIssue { get; init; } = string.Empty;

    public bool LastKyraResponseListedIssues { get; init; }

    public string LastUserGoal { get; init; } = string.Empty;

    public string LastKnownDeviceReference { get; init; } = string.Empty;

    public string LastKnownUsbReference { get; init; } = string.Empty;

    public string LastKnownToolkitIssue { get; init; } = string.Empty;

    public string LastKnownSystemIssue { get; init; } = string.Empty;

    public string LastRecommendationSummary { get; init; } = string.Empty;

    public string LastKyraAnswer { get; init; } = string.Empty;

    public string RollingSummary { get; init; } = string.Empty;
}

/// <summary>In-process Kyra chat memory with optional redacted disk persistence.</summary>
public sealed class KyraConversationMemory
{
    private readonly object _gate = new();
    private readonly List<KyraConversationTurn> _turns = [];
    private readonly int _maxTurns;
    private readonly KyraMemoryStore? _store;
    private Func<bool>? _persistEnabled;

    public KyraConversationMemory(int maxTurns = 30, KyraMemoryStore? store = null)
    {
        _maxTurns = Math.Clamp(maxTurns, 20, 30);
        _store = store;
        TryRestoreFromDisk();
    }

    /// <summary>When set, <see cref="AddTurn"/> may persist a redacted snapshot when the func returns true.</summary>
    public void SetPersistenceGate(Func<bool> persistWhenTrue)
    {
        _persistEnabled = persistWhenTrue;
    }

    public KyraIntent PreviousIntent
    {
        get
        {
            lock (_gate)
            {
                return _turns.LastOrDefault()?.Intent ?? KyraIntent.Unknown;
            }
        }
    }

    public bool AlreadyGaveDiagnosticBreakdown
    {
        get
        {
            lock (_gate)
            {
                return _turns.TakeLast(4).Any(turn => turn.GaveDiagnosticBreakdown);
            }
        }
    }

    public IReadOnlyList<KyraConversationTurn> Snapshot()
    {
        lock (_gate)
        {
            return _turns.ToArray();
        }
    }

    public CopilotChatMessage[] ToChatMessages()
    {
        lock (_gate)
        {
            return _turns
                .TakeLast(Math.Min(20, _maxTurns))
                .SelectMany(turn => new[]
                {
                    new CopilotChatMessage { Role = "You", Text = turn.UserMessage, Timestamp = turn.Timestamp },
                    new CopilotChatMessage { Role = "Kyra", Text = turn.KyraResponseSummary, Timestamp = turn.Timestamp }
                })
                .ToArray();
        }
    }

    public KyraIntent ResolveIntent(string prompt, KyraIntent detectedIntent)
    {
        var text = prompt.ToLowerInvariant();
        if (KyraFollowUpClassifier.LooksLikeRepairContinuation(text, PreviousIntent, AlreadyGaveDiagnosticBreakdown))
        {
            if (PreviousIntent is KyraIntent.SystemHealthSummary or KyraIntent.PerformanceLag or KyraIntent.StorageIssue
                or KyraIntent.DriverIssue or KyraIntent.MemoryIssue or KyraIntent.SlowBoot or KyraIntent.AppFreezing
                or KyraIntent.GPUQuestion)
            {
                return PreviousIntent;
            }

            return KyraIntent.SystemHealthSummary;
        }

        if (detectedIntent != KyraIntent.GeneralTechQuestion && detectedIntent != KyraIntent.Unknown)
        {
            return detectedIntent;
        }

        if (KyraFollowUpClassifier.LooksLikeConversationFollowUp(text))
        {
            if (PreviousIntent == KyraIntent.USBBuilderHelp)
            {
                return KyraIntent.USBBuilderHelp;
            }

            if (PreviousIntent == KyraIntent.ToolkitManagerHelp)
            {
                return KyraIntent.ToolkitManagerHelp;
            }
        }

        if (ContainsAny(text, "what did you just say", "repeat that", "summarize that", "explain that simpler", "simpler", "what would you do", "give me the commands"))
        {
            return PreviousIntent == KyraIntent.Unknown ? KyraIntent.GeneralTechQuestion : PreviousIntent;
        }

        if (ContainsAny(text, "what about the gpu", "gpu?", "graphics?"))
        {
            return KyraIntent.GPUQuestion;
        }

        if (ContainsAny(text, "is that good for flipping", "good to flip", "worth flipping"))
        {
            return KyraIntent.ResaleValue;
        }

        if (ContainsAny(text, "upgrade first", "do first", "first?"))
        {
            return KyraIntent.UpgradeAdvice;
        }

        return detectedIntent;
    }

    public void AddTurn(string prompt, string response, KyraIntent intent, SystemContext context)
    {
        lock (_gate)
        {
            _turns.Add(new KyraConversationTurn
            {
                UserMessage = CopilotRedactor.Redact(prompt),
                KyraResponseSummary = Summarize(CopilotRedactor.Redact(response)),
                Intent = intent,
                SystemSnapshot = CopilotRedactor.Redact($"{context.Device}; {context.CPU}; {context.RAM} GB RAM; {context.GPU}"),
                UnresolvedIssue = CopilotRedactor.Redact(ExtractUnresolvedIssue(prompt, intent)),
                LastRecommendation = CopilotRedactor.Redact(ExtractLastRecommendation(response)),
                GaveDiagnosticBreakdown = response.Contains("What I found", StringComparison.OrdinalIgnoreCase) ||
                                          response.Contains("What I'm seeing", StringComparison.OrdinalIgnoreCase) ||
                                          response.Contains("What I’m seeing", StringComparison.OrdinalIgnoreCase) ||
                                          response.Contains("Notable issues", StringComparison.OrdinalIgnoreCase)
            });

            if (_turns.Count > _maxTurns)
            {
                _turns.RemoveRange(0, _turns.Count - _maxTurns);
            }
        }

        if (_store is not null && (_persistEnabled?.Invoke() ?? false))
        {
            PersistSnapshot();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _turns.Clear();
        }

        _store?.ClearFile();
    }

    public KyraConversationState GetState()
    {
        lock (_gate)
        {
            return BuildStateLocked();
        }
    }

    private KyraConversationState BuildStateLocked()
    {
        var last = _turns.LastOrDefault();
        var lastNonEmptyUnresolved = _turns.Select(t => t.UnresolvedIssue).LastOrDefault(u => !string.IsNullOrWhiteSpace(u)) ?? string.Empty;
        var lastRec = _turns.Select(t => t.LastRecommendation).LastOrDefault(u => !string.IsNullOrWhiteSpace(u)) ?? string.Empty;
        var lastUsbTurn = _turns.LastOrDefault(t =>
            t.UserMessage.Contains("usb", StringComparison.OrdinalIgnoreCase) ||
            t.KyraResponseSummary.Contains("usb", StringComparison.OrdinalIgnoreCase));
        var lastToolkit = _turns.LastOrDefault(t => t.Intent == KyraIntent.ToolkitManagerHelp);
        var lastSystemIssueTurn = _turns.LastOrDefault(t =>
            !string.IsNullOrWhiteSpace(t.UnresolvedIssue) &&
            t.Intent is KyraIntent.PerformanceLag or KyraIntent.SystemHealthSummary or KyraIntent.StorageIssue
                or KyraIntent.MemoryIssue or KyraIntent.DriverIssue or KyraIntent.SlowBoot or KyraIntent.AppFreezing
                or KyraIntent.GPUQuestion);

        var deviceRef = string.Empty;
        if (last is { SystemSnapshot: var ss } && !string.IsNullOrWhiteSpace(ss))
        {
            var cut = ss.Split(';')[0].Trim();
            deviceRef = cut.Length > 200 ? cut[..200] : cut;
        }

        var userGoal = !string.IsNullOrWhiteSpace(last?.UnresolvedIssue)
            ? last!.UnresolvedIssue
            : Clip(last?.UserMessage ?? string.Empty, 160);

        var kyraAns = Clip(last?.KyraResponseSummary ?? string.Empty, 600);
        var rolling = BuildRollingSummaryLocked();

        var systemIssue = lastSystemIssueTurn?.UnresolvedIssue ?? string.Empty;
        if (string.IsNullOrWhiteSpace(systemIssue) && last?.GaveDiagnosticBreakdown == true)
        {
            systemIssue = FirstMeaningfulLine(last.KyraResponseSummary);
        }

        return new KyraConversationState
        {
            LastIntent = last?.Intent ?? KyraIntent.Unknown,
            LastKyraSummary = last?.KyraResponseSummary ?? string.Empty,
            UnresolvedIssue = lastNonEmptyUnresolved,
            LastKyraResponseListedIssues = last?.GaveDiagnosticBreakdown == true,
            LastUserGoal = userGoal,
            LastKnownDeviceReference = deviceRef,
            LastKnownUsbReference = lastUsbTurn is null ? string.Empty : Clip(lastUsbTurn.UserMessage, 200),
            LastKnownToolkitIssue = lastToolkit is null ? string.Empty : Clip(lastToolkit.UserMessage, 200),
            LastKnownSystemIssue = systemIssue,
            LastRecommendationSummary = lastRec,
            LastKyraAnswer = kyraAns,
            RollingSummary = rolling
        };
    }

    private static string FirstMeaningfulLine(string text)
    {
        foreach (var line in text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var t = line.Trim();
            if (t.Length >= 12)
            {
                return t.Length > 240 ? t[..240] : t;
            }
        }

        return string.Empty;
    }

    private static string Clip(string s, int max)
    {
        var c = s.ReplaceLineEndings(" ").Trim();
        return c.Length <= max ? c : c[..max] + "…";
    }

    private void TryRestoreFromDisk()
    {
        if (_store is null || !_store.TryLoad(out var snap) || snap.Turns.Count == 0)
        {
            return;
        }

        KyraOrchestrationLog.Append($"Kyra memory_loaded=1 turns={snap.Turns.Count}");

        lock (_gate)
        {
            foreach (var dto in snap.Turns.TakeLast(_maxTurns))
            {
                if (!Enum.TryParse(dto.Intent, out KyraIntent intent))
                {
                    intent = KyraIntent.Unknown;
                }

                _turns.Add(new KyraConversationTurn
                {
                    Timestamp = dto.Timestamp,
                    UserMessage = dto.UserMessage,
                    KyraResponseSummary = dto.KyraResponseSummary,
                    Intent = intent,
                    SystemSnapshot = dto.SystemSnapshot,
                    UnresolvedIssue = dto.UnresolvedIssue,
                    LastRecommendation = dto.LastRecommendation,
                    GaveDiagnosticBreakdown = dto.GaveDiagnosticBreakdown
                });
            }
        }
    }

    private void PersistSnapshot()
    {
        if (_store is null)
        {
            return;
        }

        KyraMemorySnapshot snap;
        KyraConversationState state;
        lock (_gate)
        {
            state = BuildStateLocked();
            snap = new KyraMemorySnapshot
            {
                Turns = _turns.Select(t => new KyraConversationTurnDto
                {
                    Timestamp = t.Timestamp,
                    UserMessage = t.UserMessage,
                    KyraResponseSummary = t.KyraResponseSummary,
                    Intent = t.Intent.ToString(),
                    SystemSnapshot = t.SystemSnapshot,
                    UnresolvedIssue = t.UnresolvedIssue,
                    LastRecommendation = t.LastRecommendation,
                    GaveDiagnosticBreakdown = t.GaveDiagnosticBreakdown
                }).ToList(),
                RollingSummary = state.RollingSummary,
                LastUserGoal = state.LastUserGoal,
                LastKyraAnswerExcerpt = state.LastKyraAnswer,
                LastIntent = state.LastIntent.ToString(),
                LastUsbReference = state.LastKnownUsbReference,
                LastRecommendedAction = state.LastRecommendationSummary,
                LastKnownDeviceReference = state.LastKnownDeviceReference,
                LastKnownUsbReference = state.LastKnownUsbReference,
                LastKnownToolkitIssue = state.LastKnownToolkitIssue,
                LastKnownSystemIssue = state.LastKnownSystemIssue,
                LastRecommendationSummary = state.LastRecommendationSummary,
                LastKyraAnswer = state.LastKyraAnswer
            };
        }

        _store.Save(snap);
        KyraOrchestrationLog.Append($"Kyra memory_saved=1 turns={snap.Turns.Count}");
    }

    private string BuildRollingSummaryLocked()
    {
        if (_turns.Count == 0)
        {
            return string.Empty;
        }

        var tail = string.Join(" | ", _turns.TakeLast(4).Select(t => $"{t.Intent}:{t.UserMessage[..Math.Min(80, t.UserMessage.Length)]}"));
        return tail.Length <= 500 ? tail : tail[..500] + "…";
    }

    private static string Summarize(string value)
    {
        var clean = value.ReplaceLineEndings(" ").Trim();
        const int max = 1_800;
        return clean.Length <= max ? clean : clean[..max] + "...";
    }

    private static string ExtractUnresolvedIssue(string prompt, KyraIntent intent)
    {
        return intent is KyraIntent.PerformanceLag or KyraIntent.AppFreezing or KyraIntent.SlowBoot
            ? prompt
            : string.Empty;
    }

    private static string ExtractLastRecommendation(string response)
    {
        var line = response
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(item => item.TrimStart().StartsWith("1.", StringComparison.OrdinalIgnoreCase));
        return line ?? string.Empty;
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
