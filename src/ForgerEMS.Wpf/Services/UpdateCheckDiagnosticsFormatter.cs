using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>Safe, clipboard-friendly update-check diagnostics (no secrets, no private paths).</summary>
public static class UpdateCheckDiagnosticsFormatter
{
    public static string BuildClipboardSummary(
        UpdateCheckResult? last,
        UpdateCheckMachineState machineState,
        string installedVersionLabel,
        bool includePrereleaseChannels)
    {
        var sb = new StringBuilder(512);
        sb.AppendLine("ForgerEMS — update check diagnostics (safe)");
        sb.Append("repo: ").Append(GitHubReleaseUpdateCheckService.DefaultOwner).Append('/')
            .AppendLine(GitHubReleaseUpdateCheckService.DefaultRepo);
        sb.Append("includePrerelease: ").AppendLine(includePrereleaseChannels ? "true" : "false");
        sb.Append("installed (raw): ").AppendLine(installedVersionLabel);
        sb.Append("installed (normalized): ").AppendLine(ReleaseVersionParser.NormalizeLabel(installedVersionLabel));
        sb.Append("machineState: ").AppendLine(UpdateCheckMachineStateResolver.Describe(machineState));

        if (last is null)
        {
            sb.AppendLine("lastResult: (none)");
            return sb.ToString().TrimEnd();
        }

        sb.Append("succeeded: ").AppendLine(last.Succeeded ? "true" : "false");
        sb.Append("outcome: ").AppendLine(last.Outcome.ToString());
        sb.Append("failureKind: ").AppendLine(last.FailureKind.ToString());
        sb.Append("releasesFetched: ").AppendLine(last.ReleasesFetchedCount.ToString(CultureInfo.InvariantCulture));
        sb.Append("selectedTag: ").AppendLine(string.IsNullOrEmpty(last.SelectedReleaseTagRaw) ? "—" : last.SelectedReleaseTagRaw);
        sb.Append("selectedPublishedAt: ").AppendLine(
            last.SelectedReleasePublishedAt is { } u
                ? u.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture)
                : "—");
        sb.Append("assetCount: ").AppendLine(last.AssetCount.ToString(CultureInfo.InvariantCulture));
        var names = last.AssetNamesSnapshot.Count == 0
            ? "—"
            : string.Join("; ", last.AssetNamesSnapshot.Take(12));
        sb.Append("assetNames (sample): ").AppendLine(names);
        sb.Append("latest (normalized): ").AppendLine(
            string.IsNullOrWhiteSpace(last.LatestVersionLabel)
                ? "—"
                : ReleaseVersionParser.NormalizeLabel(last.LatestVersionLabel));
        sb.Append("primaryAssetFound: ").AppendLine(last.SuitablePrimaryAssetFound ? "true" : "false");
        sb.Append("safeFailureReason: ").AppendLine(string.IsNullOrWhiteSpace(last.ErrorMessage) ? "—" : last.ErrorMessage);

        return sb.ToString().TrimEnd();
    }
}
