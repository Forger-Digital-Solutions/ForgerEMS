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

public sealed class LogLine
{
    private static readonly Brush InfoBrush = new SolidColorBrush(Color.FromRgb(224, 232, 244));
    private static readonly Brush SuccessBrush = new SolidColorBrush(Color.FromRgb(134, 239, 172));
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(253, 224, 71));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(248, 113, 113));

    public LogLine(DateTimeOffset timestamp, string text, LogSeverity severity, bool isErrorStream = false)
    {
        Timestamp = timestamp;
        Text = text;
        Severity = severity;
        IsErrorStream = isErrorStream;
    }

    public DateTimeOffset Timestamp { get; }

    public string Text { get; }

    public LogSeverity Severity { get; }

    public bool IsErrorStream { get; }

    public string DisplayText => $"[{Timestamp:HH:mm:ss}] {Text}";

    public Brush Foreground =>
        Severity switch
        {
            LogSeverity.Success => SuccessBrush,
            LogSeverity.Warning => WarningBrush,
            LogSeverity.Error => ErrorBrush,
            _ => InfoBrush
        };
}
