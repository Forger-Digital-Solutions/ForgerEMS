using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Infrastructure;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class TermsConsentRecord
{
    public string TermsVersion { get; init; } = string.Empty;

    public DateTimeOffset AcceptedUtc { get; init; }

    public string AppVersion { get; init; } = string.Empty;

    public string AppBuild { get; init; } = string.Empty;

    public string TermsSha256 { get; init; } = string.Empty;
}

public sealed class TermsConsentStore
{
    public const string CurrentTermsVersion = "2026-07-02.v1.2.3-preview.1";

    /// <summary>Date-only revision of the consent documents, shown separately from the app version.</summary>
    public const string CurrentTermsRevisionDate = "2026-07-02";

    public const string RequiredAgreementText =
        "I have read and agree to the ForgerEMS Terms of Use and understand the Privacy/Data Handling notes.";

    public const string RequiredSharingNoticeText =
        "I understand that logs, support bundles, Kyra context, and exported reports may contain local device/context information. I will review exported files before sharing them.";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public TermsConsentStore(string runtimeRoot)
    {
        if (string.IsNullOrWhiteSpace(runtimeRoot))
        {
            throw new ArgumentException("Runtime root is required.", nameof(runtimeRoot));
        }

        ConsentFilePath = Path.Combine(runtimeRoot, "config", "terms-consent.json");
    }

    public string ConsentFilePath { get; }

    public static string CurrentTermsSha256 => ComputeSha256(InfoDocumentTexts.BuildTermsOfService());

    public TermsConsentRecord? Load()
    {
        try
        {
            if (!File.Exists(ConsentFilePath))
            {
                return null;
            }

            var json = File.ReadAllText(ConsentFilePath);
            return JsonSerializer.Deserialize<TermsConsentRecord>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public bool HasCurrentAcceptance(string appVersion, string appBuild, out TermsConsentRecord? record)
    {
        record = Load();
        return IsCurrent(record, CurrentTermsVersion, CurrentTermsSha256, appVersion, appBuild);
    }

    public TermsConsentRecord SaveAccepted(string appVersion, string appBuild, DateTimeOffset acceptedUtc)
    {
        var record = new TermsConsentRecord
        {
            TermsVersion = CurrentTermsVersion,
            TermsSha256 = CurrentTermsSha256,
            AcceptedUtc = acceptedUtc,
            AppVersion = appVersion,
            AppBuild = appBuild
        };

        Directory.CreateDirectory(Path.GetDirectoryName(ConsentFilePath)!);
        File.WriteAllText(ConsentFilePath, JsonSerializer.Serialize(record, JsonOptions), Encoding.UTF8);
        return record;
    }

    public static bool IsCurrent(
        TermsConsentRecord? record,
        string expectedTermsVersion,
        string expectedTermsSha256,
        string expectedAppVersion,
        string expectedAppBuild)
    {
        if (record is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(record.TermsVersion) &&
               !string.IsNullOrWhiteSpace(record.TermsSha256) &&
               record.AcceptedUtc != default &&
               string.Equals(record.TermsVersion, expectedTermsVersion, StringComparison.Ordinal) &&
               string.Equals(record.TermsSha256, expectedTermsSha256, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(record.AppVersion, expectedAppVersion, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(record.AppBuild, expectedAppBuild, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
