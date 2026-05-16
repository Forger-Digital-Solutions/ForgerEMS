using ForgerEMS.Kyra.HostAdapter;

var prompt = GetArgumentValue(args, "--kyra-sdk-prompt");
var version = GetArgumentValue(args, "--kyra-sdk-version") ?? "(unknown)";

var startedUtc = DateTimeOffset.UtcNow;
var result = await KyraSdkDogfoodInvoker.InvokeAsync(new KyraSdkDogfoodOptions
{
    UserPrompt = prompt,
    HostApplicationVersion = version,
}).ConfigureAwait(false);

var lines = new List<string>(result.ToSafeReportLines())
{
    $"StartedUtc: {startedUtc:O}",
    $"FinishedUtc: {DateTimeOffset.UtcNow:O}",
    $"FORGEREMS_KYRA_SDK_ENABLED: {ForgerEmsKyraSdkFeatureFlags.IsSdkEnabledFromEnvironment()}",
};

if (!string.IsNullOrWhiteSpace(KyraSdkDogfoodEnvironment.ReadGatewayUrl()))
{
    lines.Add("GatewayUrlConfiguredFromEnv: True");
    lines.Add($"GatewayUrlRedacted: {RedactUrl(KyraSdkDogfoodEnvironment.ReadGatewayUrl())}");
}

lines.Add($"GatewayTokenPresentFromEnv: {!string.IsNullOrWhiteSpace(KyraSdkDogfoodEnvironment.ReadGatewayBetaToken())}");

var reportPath = WriteDiagnosticReport("kyra-sdk-dogfood.txt", lines);
Console.WriteLine($"Report: {reportPath}");
Console.WriteLine($"Succeeded: {result.Succeeded}");
Console.WriteLine($"ErrorCode: {result.ErrorCode ?? "(none)"}");

return result.Succeeded && File.Exists(reportPath) ? 0 : 1;

static string? GetArgumentValue(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }

    return null;
}

static string RedactUrl(string? url)
{
    if (string.IsNullOrWhiteSpace(url))
        return "(unset)";

    if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        return "[REDACTED_URL]";

    return $"{uri.Scheme}://{uri.Host}/…";
}

static string WriteDiagnosticReport(string fileName, IEnumerable<string> lines)
{
    var content = string.Join(Environment.NewLine, lines) + Environment.NewLine;
    foreach (var directory in GetDiagnosticDirectories())
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            File.WriteAllText(path, content);
            return path;
        }
        catch
        {
        }
    }

    var fallback = Path.Combine(Path.GetTempPath(), "ForgerEMS", "Runtime", "diagnostics");
    Directory.CreateDirectory(fallback);
    var fallbackPath = Path.Combine(fallback, fileName);
    File.WriteAllText(fallbackPath, content);
    return fallbackPath;
}

static IEnumerable<string> GetDiagnosticDirectories()
{
    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (!string.IsNullOrWhiteSpace(localAppData))
        yield return Path.Combine(localAppData, "ForgerEMS", "Runtime", "diagnostics");
}
