using System;
using System.IO;
using System.Linq;
using System.Reflection;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Guards the single-source-of-truth version contract: the root VERSION file is
/// authoritative, MSBuild/installer/tooling derive from it, and no frozen
/// preview defaults survive in active release plumbing.
/// </summary>
public sealed class VersionConsistencyTests
{
    [Fact]
    public void VersionFile_ParsesAsSemanticVersion_AndMatchesAssemblyInfo()
    {
        var version = File.ReadAllText(FindRepoFile("VERSION")).Trim();

        Assert.True(AppSemanticVersion.TryParse(version, out var parsed), $"VERSION '{version}' must parse as AppSemanticVersion.");
        Assert.Null(parsed.Prerelease);
        Assert.Equal(version, AppReleaseInfo.Version);
        Assert.Equal($"ForgerEMS v{version}", AppReleaseInfo.DisplayVersion);
    }

    [Fact]
    public void VersionFile_DrivesAssemblyAndFileVersion()
    {
        var version = File.ReadAllText(FindRepoFile("VERSION")).Trim();
        var assembly = typeof(AppReleaseInfo).Assembly;
        var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        var assemblyVersion = assembly.GetName().Version?.ToString();

        Assert.Equal($"{version}.0", fileVersion);
        Assert.Equal($"{version}.0", assemblyVersion);
    }

    [Fact]
    public void DirectoryBuildProps_ReadsVersionFile()
    {
        var props = File.ReadAllText(FindRepoFile("Directory.Build.props"));

        Assert.Contains("VERSION", props, StringComparison.Ordinal);
        Assert.Contains("ReadAllText", props, StringComparison.Ordinal);
        Assert.Contains("ForgerEMSReleaseVersion", props, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfProject_BindsVersionFieldsToVersionFileProperty()
    {
        var csproj = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ForgerEMS.Wpf.csproj"));

        Assert.Contains("<Version>$(ForgerEMSReleaseVersion)</Version>", csproj, StringComparison.Ordinal);
        Assert.Contains("<InformationalVersion>$(ForgerEMSReleaseVersion)</InformationalVersion>", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("<Version>1.", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("preview", csproj, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Installer_RequiresBuildProvidedVersion_AndDerivesLabels()
    {
        var iss = File.ReadAllText(FindRepoFile("installer", "ForgerEMS.iss"));

        Assert.Contains("#error AppVersion must be passed by the build script", iss, StringComparison.Ordinal);
        Assert.Contains("#error AppVersionInfo must be passed by the build script", iss, StringComparison.Ordinal);
        Assert.Contains("AppVersion={#AppVersion}", iss, StringComparison.Ordinal);
        Assert.DoesNotContain("preview", iss, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1.2.4-preview", iss, StringComparison.OrdinalIgnoreCase);
        // No literal frozen default for the app version.
        Assert.DoesNotMatch(@"#define\s+AppVersion\s+""", iss);
    }

    [Fact]
    public void BuildScripts_ReadVersionFile_AndRefuseOverrides()
    {
        foreach (var script in new[] { "build-release.ps1", "build-forgerems-installer.ps1" })
        {
            var text = File.ReadAllText(FindRepoFile("tools", script));

            Assert.Contains("VERSION", text, StringComparison.Ordinal);
            Assert.Contains("Refusing to override authoritative VERSION", text, StringComparison.Ordinal);
        }

        var stagePrerelease = File.ReadAllText(FindRepoFile("tools", "stage-prerelease.ps1"));
        Assert.Contains("VERSION", stagePrerelease, StringComparison.Ordinal);
        Assert.DoesNotContain("preview.", stagePrerelease, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseNotes_ExistForAuthoritativeVersion()
    {
        var version = File.ReadAllText(FindRepoFile("VERSION")).Trim();

        Assert.True(
            File.Exists(FindRepoFile("docs", $"RELEASE_NOTES_v{version}.md")),
            $"docs/RELEASE_NOTES_v{version}.md must exist for the authoritative version.");
    }

    [Fact]
    public void ContinuousIntegration_DryRun_RequiresNoFrozenVersion()
    {
        var workflow = File.ReadAllText(FindRepoFile(".github", "workflows", "build.yml"));

        Assert.Contains("build-release.ps1 -DryRun", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("1.0.0-beta.ci", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("-Version 1.", workflow, StringComparison.Ordinal);
    }

    private static string FindRepoFile(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate repo file: {Path.Combine(segments)}");
    }
}
