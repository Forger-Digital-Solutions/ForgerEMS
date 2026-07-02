using System;
using System.Windows.Media;

namespace VentoyToolkitSetup.Wpf.Models;

public enum LogSeverity
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

/// <summary>Live Logs channel: operation-focused vs Kyra internals (hidden unless verbose).</summary>
public enum LiveLogChannel
{
    Operation,
    KyraDetail,
    Update,
    Diagnostics
}

public sealed class LogLine
{
    private static readonly Brush InfoBrush = new SolidColorBrush(Color.FromRgb(224, 232, 244));
    private static readonly Brush SuccessBrush = new SolidColorBrush(Color.FromRgb(134, 239, 172));
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(253, 224, 71));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(248, 113, 113));

    public LogLine(DateTimeOffset timestamp, string text, LogSeverity severity, bool isErrorStream = false, LiveLogChannel channel = LiveLogChannel.Operation)
    {
        Timestamp = timestamp;
        Text = text;
        Severity = severity;
        IsErrorStream = isErrorStream;
        Channel = channel;
    }

    public DateTimeOffset Timestamp { get; }

    public string Text { get; }

    public LogSeverity Severity { get; }

    public bool IsErrorStream { get; }

    public LiveLogChannel Channel { get; }

    public string DisplayText
    {
        get
        {
            if (StartsWithTimestamp(Text))
            {
                return Text;
            }

            return !string.IsNullOrEmpty(Text) && Text[0] == '['
                ? $"[{Timestamp:HH:mm:ss}]{Text}"
                : $"[{Timestamp:HH:mm:ss}] {Text}";
        }
    }

    public Brush Foreground =>
        Severity switch
        {
            LogSeverity.Success => SuccessBrush,
            LogSeverity.Warning => WarningBrush,
            LogSeverity.Error => ErrorBrush,
            _ => InfoBrush
        };

    private static bool StartsWithTimestamp(string value)
    {
        if (value.Length >= 10 &&
            value[0] == '[' &&
            value[3] == ':' &&
            value[6] == ':' &&
            value[9] == ']' &&
            char.IsDigit(value[1]) &&
            char.IsDigit(value[2]) &&
            char.IsDigit(value[4]) &&
            char.IsDigit(value[5]) &&
            char.IsDigit(value[7]) &&
            char.IsDigit(value[8]))
        {
            return true;
        }

        return value.Length >= 21 &&
               value[0] == '[' &&
               value[5] == '-' &&
               value[8] == '-' &&
               (value[11] == ' ' || value[11] == 'T') &&
               value[14] == ':' &&
               value[17] == ':' &&
               value[20] == ']' &&
               char.IsDigit(value[1]) &&
               char.IsDigit(value[2]) &&
               char.IsDigit(value[3]) &&
               char.IsDigit(value[4]) &&
               char.IsDigit(value[6]) &&
               char.IsDigit(value[7]) &&
               char.IsDigit(value[9]) &&
               char.IsDigit(value[10]) &&
               char.IsDigit(value[12]) &&
               char.IsDigit(value[13]) &&
               char.IsDigit(value[15]) &&
               char.IsDigit(value[16]) &&
               char.IsDigit(value[18]) &&
               char.IsDigit(value[19]);
    }
}
