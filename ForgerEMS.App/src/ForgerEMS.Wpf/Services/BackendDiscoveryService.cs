using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public interface IBackendDiscoveryService
{
    BackendContext Discover(string? preferredStartPath = null);
}

public sealed class BackendDiscoveryService : IBackendDiscoveryService
{
    public BackendContext Discover(string? preferredStartPath = null)
    {
        var searchedRoots = GetCandidateRoots(preferredStartPath).ToList();

        foreach (var root in searchedRoots)
        {
            var repoScriptsRoot = Path.Combine(root, "ventoy-core");
            if (HasScripts(repoScriptsRoot))
            {
                return new BackendContext
                {
                    IsAvailable = true,
                    Mode = BackendMode.Repo,
                    RootPath = root,
                    WorkingDirectory = root,
                    VerifyScriptPath = Path.Combine(repoScriptsRoot, "Verify-VentoyCore.ps1"),
                    SetupScriptPath = Path.Combine(repoScriptsRoot, "Setup-ForgerEMS.ps1"),
                    UpdateScriptPath = Path.Combine(repoScriptsRoot, "Update-ForgerEMS.ps1"),
                    DiagnosticMessage = "Repo-mode backend discovered."
                };
            }

            if (HasScripts(root) && HasReleaseBundleMarkers(root))
            {
                return new BackendContext
                {
                    IsAvailable = true,
                    Mode = BackendMode.ReleaseBundle,
                    RootPath = root,
                    WorkingDirectory = root,
                    VerifyScriptPath = Path.Combine(root, "Verify-VentoyCore.ps1"),
                    SetupScriptPath = Path.Combine(root, "Setup-ForgerEMS.ps1"),
                    UpdateScriptPath = Path.Combine(root, "Update-ForgerEMS.ps1"),
                    DiagnosticMessage = "Release-bundle backend discovered."
                };
            }
        }

        var message = searchedRoots.Count == 0
            ? "No searchable roots were available."
            : "Could not find Verify-VentoyCore.ps1, Setup-ForgerEMS.ps1, and Update-ForgerEMS.ps1 in repo mode or release-bundle mode. Searched: " +
              string.Join(" | ", searchedRoots.Take(8));

        return BackendContext.Unavailable(message);
    }

    private static bool HasScripts(string root)
    {
        return File.Exists(Path.Combine(root, "Verify-VentoyCore.ps1")) &&
               File.Exists(Path.Combine(root, "Setup-ForgerEMS.ps1")) &&
               File.Exists(Path.Combine(root, "Update-ForgerEMS.ps1"));
    }

    private static bool HasReleaseBundleMarkers(string root)
    {
        return File.Exists(Path.Combine(root, "VERSION.txt")) ||
               File.Exists(Path.Combine(root, "RELEASE-BUNDLE.txt"));
    }

    private static IEnumerable<string> GetCandidateRoots(string? preferredStartPath)
    {
        var seeds = new List<string>();

        if (!string.IsNullOrWhiteSpace(preferredStartPath))
        {
            seeds.Add(preferredStartPath);
        }

        seeds.Add(Directory.GetCurrentDirectory());
        seeds.Add(AppContext.BaseDirectory);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in seeds.Where(Directory.Exists))
        {
            var current = Path.GetFullPath(seed);
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (seen.Add(current))
                {
                    yield return current;
                }

                var parent = Directory.GetParent(current);
                if (parent is null)
                {
                    break;
                }

                current = parent.FullName;
            }
        }
    }
}
