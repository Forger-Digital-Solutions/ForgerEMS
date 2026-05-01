using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>
/// Checks public GitHub Releases for a newer ForgerEMS version. No token required for public repos.
/// </summary>
public sealed class GitHubReleaseUpdateCheckService : IUpdateCheckService, IDisposable
{
    public const string DefaultOwner = "Forger-Digital-Solutions";
    public const string DefaultRepo = "ForgerEMS";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public GitHubReleaseUpdateCheckService(HttpClient? httpClient = null)
    {
        if (httpClient is not null)
        {
            _httpClient = httpClient;
            _ownsClient = false;
            return;
        }

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ForgerEMS-UpdateCheck/1.1.4");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        _ownsClient = true;
    }

    public async Task<UpdateCheckResult> CheckForNewerReleaseAsync(Version currentVersion, string? ignoredVersionNormalized, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient
                .GetAsync($"https://api.github.com/repos/{DefaultOwner}/{DefaultRepo}/releases/latest", cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult
                {
                    Succeeded = false,
                    ErrorMessage = $"GitHub returned HTTP {(int)response.StatusCode}. You may be offline or the release API is unavailable."
                };
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, default, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (!root.TryGetProperty("tag_name", out var tagProp))
            {
                return new UpdateCheckResult { Succeeded = false, ErrorMessage = "Release response missing tag_name." };
            }

            var tag = tagProp.GetString() ?? string.Empty;
            var latestVersion = ReleaseVersionParser.TryParseVersion(tag, out var v) ? v : null;
            var label = ReleaseVersionParser.NormalizeLabel(tag);

            var notesUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? string.Empty : string.Empty;

            string? installerUrl = null;
            string? installerName = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (!asset.TryGetProperty("name", out var nameProp) ||
                        !asset.TryGetProperty("browser_download_url", out var dlProp))
                    {
                        continue;
                    }

                    var name = nameProp.GetString() ?? string.Empty;
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (name.Contains("ForgerEMS", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                    {
                        installerName = name;
                        installerUrl = dlProp.GetString();
                        break;
                    }
                }

                if (installerUrl is null)
                {
                    var fallback = assets.EnumerateArray().FirstOrDefault(a =>
                        a.TryGetProperty("name", out var n) &&
                        (n.GetString() ?? string.Empty).EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                    if (fallback.ValueKind != JsonValueKind.Undefined &&
                        fallback.TryGetProperty("browser_download_url", out var dl) &&
                        fallback.TryGetProperty("name", out var fn))
                    {
                        installerName = fn.GetString();
                        installerUrl = dl.GetString();
                    }
                }
            }

            if (latestVersion is null)
            {
                return new UpdateCheckResult
                {
                    Succeeded = true,
                    UpdateAvailable = false,
                    LatestVersionLabel = label,
                    ReleaseNotesUrl = notesUrl,
                    ErrorMessage = "Could not parse a version from the latest release tag."
                };
            }

            var ignored = ReleaseVersionParser.NormalizeIgnored(ignoredVersionNormalized);
            if (!string.IsNullOrEmpty(ignored) &&
                string.Equals(ReleaseVersionParser.NormalizeLabel(label), ignored, StringComparison.OrdinalIgnoreCase))
            {
                return new UpdateCheckResult
                {
                    Succeeded = true,
                    UpdateAvailable = false,
                    LatestVersion = latestVersion,
                    LatestVersionLabel = label,
                    ReleaseNotesUrl = notesUrl,
                    InstallerAssetName = installerName,
                    InstallerDownloadUrl = installerUrl
                };
            }

            var newer = latestVersion > currentVersion;
            return new UpdateCheckResult
            {
                Succeeded = true,
                UpdateAvailable = newer,
                LatestVersion = latestVersion,
                LatestVersionLabel = label,
                ReleaseNotesUrl = notesUrl,
                InstallerAssetName = installerName,
                InstallerDownloadUrl = installerUrl
            };
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult { Succeeded = false, ErrorMessage = "Update check was cancelled." };
        }
        catch (Exception exception)
        {
            return new UpdateCheckResult
            {
                Succeeded = false,
                ErrorMessage = $"Update check failed: {exception.Message}"
            };
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}

public static class ReleaseVersionParser
{
    public static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var trimmed = tag.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        var core = trimmed;
        var plus = core.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            core = core[..plus];
        }

        var dash = core.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            core = core[..dash];
        }

        if (!Version.TryParse(core, out var parsed))
        {
            return false;
        }

        version = parsed;
        return true;
    }

    public static string NormalizeLabel(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        var t = tag.Trim();
        if (t.StartsWith('v') || t.StartsWith('V'))
        {
            t = t[1..];
        }

        return t;
    }

    public static string NormalizeIgnored(string? ignored)
    {
        if (string.IsNullOrWhiteSpace(ignored))
        {
            return string.Empty;
        }

        return NormalizeLabel(ignored);
    }
}

public static class UpdateNotificationTextBuilder
{
    public static string BuildHeadline(string latestLabel)
        => $"Update available: ForgerEMS v{ReleaseVersionParser.NormalizeLabel(latestLabel)}. Download now?";
}
