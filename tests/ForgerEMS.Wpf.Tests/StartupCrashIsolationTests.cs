using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Startup crash-report writes must honor the process-level LOCALAPPDATA
/// override first (same contract as StartupDiagnosticLog / AppRuntimeService)
/// so isolated profiles never leak crash reports into the real user profile.
/// </summary>
public sealed class StartupCrashIsolationTests
{
    [Fact]
    public void StartupCrashWriteCandidates_HonorProcessLocalAppDataOverride()
    {
        var isolated = Path.Combine(Path.GetTempPath(), "fe-startup-iso-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolated);
        var previous = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        try
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", isolated);

            var candidates = EnumerateStartupCrashCandidates().ToArray();
            Assert.NotEmpty(candidates);

            var isolatedRoot = Path.GetFullPath(isolated).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            // Every profile-rooted candidate must live under the isolated LOCALAPPDATA;
            // the temp fallback is permitted and may legitimately sit elsewhere.
            foreach (var candidate in candidates)
            {
                var full = Path.GetFullPath(candidate);
                var underTemp = full.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase);
                var underIsolated = full.StartsWith(isolatedRoot, StringComparison.OrdinalIgnoreCase);
                Assert.True(underIsolated || underTemp,
                    $"Candidate '{full}' is outside the isolated LOCALAPPDATA root and is not the temp fallback.");
            }

            // At least one candidate must actually use the isolated root — otherwise the
            // override was silently ignored.
            Assert.Contains(candidates, c =>
                Path.GetFullPath(c).StartsWith(isolatedRoot, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOCALAPPDATA", previous);
            try
            {
                Directory.Delete(isolated, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static IEnumerable<string> EnumerateStartupCrashCandidates()
    {
        // The WPF App class keeps its historical VentoyToolkitSetup.Wpf namespace.
        var method = typeof(VentoyToolkitSetup.Wpf.App).GetMethod(
            "GetStartupCrashWriteCandidates",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var result = method!.Invoke(null, null) as IEnumerable<string>;
        Assert.NotNull(result);
        return result!;
    }
}
