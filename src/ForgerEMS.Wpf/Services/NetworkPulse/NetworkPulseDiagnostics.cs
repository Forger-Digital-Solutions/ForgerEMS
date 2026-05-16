using System;
using System.Security.Cryptography;
using System.Text;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

/// <summary>Rate-limited session logging for Network Pulse (avoid spamming identical errors).</summary>
public sealed class NetworkPulseDiagnostics
{
    private readonly Action<LogLine>? _sink;
    private readonly TimeSpan _minRepeat;
    private string _lastSignature = string.Empty;
    private DateTimeOffset _lastEmitUtc = DateTimeOffset.MinValue;

    public NetworkPulseDiagnostics(Action<LogLine>? sink, TimeSpan? minRepeat = null)
    {
        _sink = sink;
        _minRepeat = minRepeat ?? TimeSpan.FromMinutes(2);
    }

    public void Emit(string message, LogSeverity severity = LogSeverity.Warning)
    {
        if (_sink is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var sig = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message)))[..16];
        if (sig == _lastSignature && (now - _lastEmitUtc) < _minRepeat)
        {
            return;
        }

        _lastSignature = sig;
        _lastEmitUtc = now;
        _sink(new LogLine(now, $"Network Pulse: {message}", severity, isErrorStream: false, LiveLogChannel.Diagnostics));
    }
}
