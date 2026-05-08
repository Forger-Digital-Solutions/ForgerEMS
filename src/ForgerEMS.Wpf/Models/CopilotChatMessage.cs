using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Models;

public sealed partial class CopilotChatMessage : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private bool _showTroubleshootingFeedback;

    public string Role { get; init; } = "Assistant";

    public string Text
    {
        get => _text;
        init => _text = NormalizeChatText(value);
    }

    public string SourceLabel { get; init; } = string.Empty;

    public bool OnlineEnhancementApplied { get; init; }

    public string MetadataSummary { get; init; } = string.Empty;

    public string MetadataDetails { get; init; } = string.Empty;

    public bool HasMetadataSummary =>
        !string.IsNullOrWhiteSpace(MetadataSummary);

    public bool HasMetadataDetails =>
        !string.IsNullOrWhiteSpace(MetadataSummary) || !string.IsNullOrWhiteSpace(MetadataDetails);

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>User question that produced this Kyra reply (for local learning feedback).</summary>
    public string? LearningUserPrompt { get; init; }

    /// <summary>Kyra answer body before optional action-suggestion footer (for learning).</summary>
    public string? LearningKyraResponsePlain { get; init; }

    public KyraIntent LearningIntent { get; init; }

    public bool ShowTroubleshootingFeedback
    {
        get => _showTroubleshootingFeedback;
        set
        {
            if (_showTroubleshootingFeedback == value)
            {
                return;
            }

            _showTroubleshootingFeedback = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(KyraFeedbackPanelVisibility));
        }
    }

    public Visibility KyraFeedbackPanelVisibility =>
        Role.Equals("Kyra", StringComparison.OrdinalIgnoreCase) && ShowTroubleshootingFeedback
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string DisplayText => $"{Role}: {Text}";

    public Visibility CopyVisibility => Role.Equals("Kyra", StringComparison.OrdinalIgnoreCase)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public static string NormalizeChatText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.ReplaceLineEndings(Environment.NewLine);
        normalized = CodeFenceLineRegex().Replace(normalized, string.Empty);
        normalized = normalized.Replace("**", string.Empty, StringComparison.Ordinal);
        normalized = InlineCodeRegex().Replace(normalized, "$1");
        return normalized.TrimEnd();
    }

    [GeneratedRegex(@"(?m)^\s*```[A-Za-z0-9_-]*\s*$\r?\n?", RegexOptions.Compiled)]
    private static partial Regex CodeFenceLineRegex();

    [GeneratedRegex(@"`([^`\r\n]+)`", RegexOptions.Compiled)]
    private static partial Regex InlineCodeRegex();
}
