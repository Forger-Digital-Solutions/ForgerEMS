using System;
using System.Collections.Generic;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public sealed record ElevatedScanStartupRequest(
    bool OpenSystemIntelligence,
    bool RunElevatedScan,
    string RequestId)
{
    public const string OpenSystemIntelligenceArg = "--open-system-intelligence";
    public const string RunElevatedScanArg = "--run-elevated-scan";
    public const string RequestIdArg = "--elevated-scan-request-id";

    public static ElevatedScanStartupRequest None { get; } = new(false, false, string.Empty);

    public bool HasPendingElevatedScan => RunElevatedScan && !string.IsNullOrWhiteSpace(RequestId);

    public static ElevatedScanStartupRequest Parse(IEnumerable<string> args)
    {
        var openSystemIntelligence = false;
        var runElevatedScan = false;
        var requestId = string.Empty;
        var items = args as string[] ?? [.. args];
        for (var i = 0; i < items.Length; i++)
        {
            var arg = items[i];
            if (string.Equals(arg, OpenSystemIntelligenceArg, StringComparison.OrdinalIgnoreCase))
            {
                openSystemIntelligence = true;
                continue;
            }

            if (string.Equals(arg, RunElevatedScanArg, StringComparison.OrdinalIgnoreCase))
            {
                runElevatedScan = true;
                continue;
            }

            if (string.Equals(arg, RequestIdArg, StringComparison.OrdinalIgnoreCase) &&
                i + 1 < items.Length)
            {
                requestId = NormalizeRequestId(items[++i]);
            }
        }

        return new ElevatedScanStartupRequest(openSystemIntelligence, runElevatedScan, requestId);
    }

    public static string CreateRequestId() => Guid.NewGuid().ToString("N");

    public static void AddArguments(ProcessStartInfoBuilder builder, string requestId)
    {
        builder.Add(OpenSystemIntelligenceArg);
        builder.Add(RunElevatedScanArg);
        builder.Add(RequestIdArg);
        builder.Add(NormalizeRequestId(requestId));
    }

    private static string NormalizeRequestId(string value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return Guid.TryParse(trimmed, out var parsed)
            ? parsed.ToString("N")
            : string.Empty;
    }
}

public sealed class ProcessStartInfoBuilder
{
    private readonly List<string> _arguments = [];

    public IReadOnlyList<string> Arguments => _arguments;

    public void Add(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _arguments.Add(value);
        }
    }
}
