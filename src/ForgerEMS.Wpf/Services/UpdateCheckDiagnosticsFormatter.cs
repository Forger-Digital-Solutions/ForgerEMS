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

    /// <summary>Short, log-safe excerpt from GitHub HTTP diagnostics (no tokens).</summary>
    public static string RedactForLog(string? diagnosticDetail)
    {
        if (string.IsNullOrWhiteSpace(diagnosticDetail))
        {
            return string.Empty;
        }

        var flat = diagnosticDetail.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (flat.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
        {
            return "GitHub API rate limit (HTTP 403/429).";
        }

        return flat.Length <= 120 ? flat : flat[..120] + "…";
    }

    public static bool IsGitHubRateLimited(UpdateCheckResult result)
    {
        if (result.FailureKind != UpdateCheckFailureKind.AccessDeniedOrRateLimited)
        {
            return false;
        }

        var haystack = $"{result.ErrorMessage} {result.DiagnosticDetail}";
        return haystack.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>User-friendly live-log line for common non-blocking update-check outcomes.</summary>
    public static string? TryFormatLiveLogMessage(UpdateCheckResult result, bool manual)
    {
        if (IsGitHubRateLimited(result))
        {
            return manual
                ? "Update check paused: GitHub API rate limit (temporary). Installed version unchanged — try again in a few minutes from Settings → App updates."
                : "Update check paused: GitHub API rate limit (temporary). Installed version unchanged — try again later from Settings → App updates.";
        }

        return result.FailureKind switch
        {
            UpdateCheckFailureKind.Network =>
                "Update check did not complete: network unavailable. Installed version unchanged.",
            UpdateCheckFailureKind.Timeout =>
                manual
                    ? "Update check did not complete: timed out after 15 seconds. Installed version unchanged."
                    : "Update check did not complete: background check timed out. Installed version unchanged.",
            UpdateCheckFailureKind.UpdateSourceUnreachable =>
                "Update check did not complete: update source unreachable. Installed version unchanged.",
            _ => null
        };
    }
}
