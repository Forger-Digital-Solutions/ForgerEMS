using System.IO;

namespace VentoyToolkitSetup.Wpf.Services;

public static class KnownSecurityTestFixtureRecognizer
{
    public const string InternalSelfTestFileName = "forgerems-simulated-risk-test.fakeexe";
    public const string InternalSelfTestPayload = "FORGEREMS-QUARANTINE-PIPELINE-TEST-NOT-MALWARE";

    private static readonly HashSet<string> AmtsoHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "amtso.org",
        "www.amtso.org"
    };

    private static readonly HashSet<string> EicarHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "eicar.org",
        "www.eicar.org",
        "secure.eicar.org"
    };

    private static readonly string[] AmtsoFixturePaths =
    [
        "/security-features-check",
        "/feature-settings-check-phishing-page",
        "/feature-settings-check-download-of-malware",
        "/feature-settings-check-drive-by-download",
        "/feature-settings-check-potentially-unwanted-applications",
        "/feature-settings-check-compressed-malware"
    ];

    private static readonly string[] EicarFixturePaths =
    [
        "eicar.com.txt",
        "eicar.com",
        "eicar_com.zip",
        "eicar_com2.zip"
    ];

    private static readonly HashSet<string> EicarFixtureFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "eicar.com.txt",
        "eicar.com",
        "eicar_com.zip",
        "eicar_com2.zip"
    };

    public static KnownSecurityTestFixture Recognize(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return KnownSecurityTestFixture.None;
        }

        var host = NormalizeHost(uri.IdnHost);
        var path = uri.AbsolutePath.ToLowerInvariant();

        if (AmtsoHosts.Contains(host) && AmtsoFixturePaths.Any(path.Contains))
        {
            if (path.Contains("/feature-settings-check-phishing-page", StringComparison.Ordinal))
            {
                return Create(
                    "AMTSO simulated phishing test",
                    "AMTSO security-feature test. Safe test fixture, but should be treated as dangerous for validation.",
                    SafetyCheckSeverity.SimulatedPhishingTestFixture);
            }

            if (path.Contains("/feature-settings-check-download-of-malware", StringComparison.Ordinal) ||
                path.Contains("/feature-settings-check-drive-by-download", StringComparison.Ordinal) ||
                path.Contains("/feature-settings-check-compressed-malware", StringComparison.Ordinal) ||
                path.Contains("/feature-settings-check-potentially-unwanted-applications", StringComparison.Ordinal))
            {
                return Create(
                    "AMTSO simulated malware download test",
                    "AMTSO security-feature test. Safe test fixture, but should be treated as dangerous for validation.",
                    SafetyCheckSeverity.SimulatedMalwareTestFixture);
            }

            return Create(
                "AMTSO security-feature test",
                "AMTSO security-feature test. Safe test fixture, but should be treated as dangerous for validation.",
                SafetyCheckSeverity.KnownSafeSecurityTestFixture);
        }

        if (EicarHosts.Contains(host) && EicarFixturePaths.Any(path.Contains))
        {
            return Create(
                "EICAR anti-malware test file",
                "EICAR anti-malware test file. Safe test fixture. AV may block/delete it during download.",
                SafetyCheckSeverity.SimulatedMalwareTestFixture);
        }

        return KnownSecurityTestFixture.None;
    }

    public static KnownSecurityTestFixture RecognizeLocalFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return KnownSecurityTestFixture.None;
        }

        var name = Path.GetFileName(filePath.Trim());
        if (EicarFixtureFileNames.Contains(name))
        {
            return Create(
                "EICAR anti-malware test file",
                "EICAR anti-malware test file. Safe test fixture. AV may block/delete it during reads or quarantine.",
                SafetyCheckSeverity.SimulatedMalwareTestFixture);
        }

        if (string.Equals(name, InternalSelfTestFileName, StringComparison.OrdinalIgnoreCase))
        {
            return Create(
                "ForgerEMS internal quarantine pipeline test",
                "ForgerEMS harmless self-test payload. It validates quarantine plumbing only and does not test antivirus detection.",
                SafetyCheckSeverity.SimulatedMalwareTestFixture);
        }

        return KnownSecurityTestFixture.None;
    }

    private static KnownSecurityTestFixture Create(
        string name,
        string description,
        SafetyCheckSeverity primarySeverity)
    {
        IReadOnlyList<SafetyCheckSeverity> states = primarySeverity == SafetyCheckSeverity.KnownSafeSecurityTestFixture
            ? new[] { SafetyCheckSeverity.KnownSafeSecurityTestFixture }
            : new[] { SafetyCheckSeverity.KnownSafeSecurityTestFixture, primarySeverity };

        return new KnownSecurityTestFixture
        {
            IsKnown = true,
            Name = name,
            Description = description,
            PrimarySeverity = primarySeverity,
            Classifications = states
        };
    }

    private static string NormalizeHost(string host)
    {
        return host.Trim().TrimEnd('.').ToLowerInvariant();
    }
}
