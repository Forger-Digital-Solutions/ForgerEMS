using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class UsbBuilderProfileAndCrossPlatformCatalogTests
{
    private static readonly string[] CrossPlatformManualCategoryIds = ["macos", "android", "ios-ipados"];

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ForgerEMS.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("Could not locate ForgerEMS.sln.");
        }
    }

    private static string SourceManifestPath =>
        Path.Combine(RepoRoot, "manifests", "ForgerEMS.updates.json");

    [Fact]
    public void UsbBuilderProfile_DefaultsKeepCoreAndOptOutMacMobile()
    {
        var settings = UsbBuilderProfileSettingsStore.ApplyDefaults(new UsbBuilderProfileSettings());

        Assert.Contains("core", settings.IncludedCategoryIds);
        Assert.Contains("windows", settings.IncludedCategoryIds);
        Assert.Contains("legacy-windows", settings.IncludedCategoryIds);
        Assert.Contains("linux-rescue", settings.IncludedCategoryIds);
        Assert.Contains("diagnostics", settings.IncludedCategoryIds);
        Assert.DoesNotContain("macos", settings.IncludedCategoryIds);
        Assert.DoesNotContain("android", settings.IncludedCategoryIds);
        Assert.DoesNotContain("ios-ipados", settings.IncludedCategoryIds);
    }

    [Fact]
    public void UsbBuilderProfile_CoreCannotBeDisabled()
    {
        var settings = UsbBuilderProfileSettingsStore.ApplyDefaults(new UsbBuilderProfileSettings
        {
            IncludedCategoryIds = ["macos"]
        });

        Assert.Contains("core", settings.IncludedCategoryIds);
        Assert.Contains("macos", settings.IncludedCategoryIds);
    }

    [Theory]
    [InlineData("macos", false, true)]
    [InlineData("android", false, true)]
    [InlineData("ios-ipados", false, true)]
    public void ManifestBuilderCategories_MobileAndMacAreManualOptIn(string categoryId, bool defaultIncluded, bool requiresManual)
    {
        var category = BuilderCategory(categoryId);
        Assert.Equal(defaultIncluded, category.GetProperty("defaultIncluded").GetBoolean());
        Assert.Equal(requiresManual, category.GetProperty("requiresManualMedia").GetBoolean());
    }

    [Theory]
    [InlineData(@"ISO\macOS\macOS-Manual-Installer-Drop\Sequoia")]
    [InlineData(@"ISO\macOS\macOS-Manual-Installer-Drop\Sonoma")]
    [InlineData(@"ISO\Android\Android-Manual-Firmware-Drop\Google Pixel")]
    [InlineData(@"ISO\Android\Android-Manual-Firmware-Drop\Samsung")]
    [InlineData(@"Tools\Android")]
    [InlineData(@"ISO\iOS-iPadOS\iOS-Manual-IPSW-Drop\iPhone")]
    [InlineData(@"ISO\iOS-iPadOS\iOS-Manual-IPSW-Drop\iPad")]
    [InlineData(@"Tools\Apple-Mobile")]
    public void ManifestExtras_DeclaresCrossPlatformDropDirectories(string expected)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SourceManifestPath));
        var dirs = document.RootElement.GetProperty("extras").GetProperty("seedDirectories")
            .EnumerateArray()
            .Select(x => x.GetString() ?? string.Empty);

        Assert.Contains(expected, dirs);
    }

    [Fact]
    public void CatalogActionLabels_FollowApprovedTaxonomy()
    {
        // Every catalog .url filename's leading action label must come from the approved set.
        // 'INFO' must not be used as filler for missing downloads — true how-to pages get GUIDE,
        // download landing pages get DOWNLOAD or MANUAL DOWNLOAD, user-supplied media gets MANUAL ... REQUIRED.
        var approvedPrefixes = new[]
        {
            "AUTO DOWNLOAD - ",
            "DOWNLOAD - ",
            "MANUAL DOWNLOAD - ",
            "MANUAL ISO REQUIRED - ",
            "MANUAL INSTALLER REQUIRED - ",
            "MANUAL IPSW REQUIRED - ",
            "MANUAL FIRMWARE REQUIRED - ",
            "MANUAL MEDIA REQUIRED - ",
            "GUIDE - "
        };

        // Any .url filename whose leaf contains a how-to verb must use GUIDE.
        var guideVerbs = new[] { "restore", "recovery", "create bootable", "createinstallmedia", "configurator", "build guide", "install guide", "how to" };

        foreach (var item in Items())
        {
            var dest = GetString(item, "dest");
            if (string.IsNullOrWhiteSpace(dest) || !dest.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var leaf = Path.GetFileName(dest);
            Assert.True(
                approvedPrefixes.Any(p => leaf.StartsWith(p, StringComparison.Ordinal)),
                $"Catalog .url filename does not start with an approved action label: {dest}");

            Assert.False(leaf.StartsWith("INFO - ", StringComparison.Ordinal),
                $"Catalog .url filename must use a precise action label instead of INFO: {dest}");

            var leafLower = leaf.ToLowerInvariant();
            foreach (var verb in guideVerbs)
            {
                Assert.False(
                    leafLower.Contains(verb) && !leaf.StartsWith("GUIDE - ", StringComparison.Ordinal),
                    $"How-to shortcut should use GUIDE (verb '{verb}'): {dest}.");
            }
        }
    }

    [Fact]
    public void ManualMediaItems_NeverShipChecksumOrDirectFileFields()
    {
        var manualOnlyCategories = new HashSet<string>(["macos", "android", "ios-ipados", "legacy-windows"], StringComparer.OrdinalIgnoreCase);

        foreach (var item in Items())
        {
            var categoryId = GetString(item, "categoryId");
            if (!manualOnlyCategories.Contains(categoryId))
            {
                continue;
            }

            var name = GetString(item, "name");

            Assert.False(item.TryGetProperty("sha256", out _),
                $"Manual-media catalog item must not declare a sha256 field: {name} ({categoryId})");
            Assert.False(item.TryGetProperty("sha256Url", out _),
                $"Manual-media catalog item must not declare a sha256Url field: {name} ({categoryId})");
            Assert.False(item.TryGetProperty("sha512", out _),
                $"Manual-media catalog item must not declare a sha512 field: {name} ({categoryId})");
            Assert.False(item.TryGetProperty("sha512Url", out _),
                $"Manual-media catalog item must not declare a sha512Url field: {name} ({categoryId})");

            var type = GetString(item, "type");
            Assert.True(string.Equals(type, "page", StringComparison.OrdinalIgnoreCase),
                $"Manual-media catalog item must use type='page', not type='{type}': {name} ({categoryId})");
        }
    }

    [Fact]
    public void CrossPlatformCatalog_UsesOnlyOfficialAllowedHosts()
    {
        var allowedHostsByCategory = new Dictionary<string, string[]>
        {
            ["macos"] = ["apple.com"],
            ["ios-ipados"] = ["apple.com"],
            ["android"] = ["android.com", "google.com", "samsung.com", "motorola.com", "oneplus.com"]
        };

        foreach (var item in Items().Where(item => allowedHostsByCategory.ContainsKey(GetString(item, "categoryId"))))
        {
            var url = GetString(item, "url");
            Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var uri), $"Invalid URL: {url}");
            Assert.Equal("https", uri!.Scheme);
            var allowed = allowedHostsByCategory[GetString(item, "categoryId")];
            Assert.Contains(allowed, suffix => uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void CrossPlatformCatalog_DoesNotUseBannedMirrorHosts()
    {
        var banned = new[]
        {
            "archive.org/details/MS_",
            "winworldpc",
            "softlay",
            "mediafire",
            "ipsw.me",
            "sammobile",
            "samfw",
            "androidfilehost",
            "firmwarefile",
            "thepiratebay",
            "1337x"
        };

        foreach (var item in Items())
        {
            var url = GetString(item, "url");
            foreach (var fragment in banned)
            {
                Assert.DoesNotContain(fragment, url, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void CrossPlatformReadmes_IncludeManualRedistributionWarnings()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SourceManifestPath));
        var readmes = document.RootElement.GetProperty("extras").GetProperty("readmes")
            .EnumerateArray()
            .Where(r => CrossPlatformManualCategoryIds.Contains(GetString(r, "categoryId")))
            .ToArray();

        Assert.True(readmes.Length >= 6, "Expected cross-platform README extras.");
        foreach (var readme in readmes)
        {
            var body = ConcatBody(readme);
            Assert.Contains("do not use random internet ISO mirrors", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("user-supplied", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ForgerEMS does not redistribute", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void UpdateScript_ProfileSelectionSeedsOnlySelectedExtrasAndKeepsExistingFiles()
    {
        using var temp = new TempFolder();
        var manifestPath = WriteProfileTestManifest(temp.Path);
        var existingAndroidFile = Path.Combine(temp.Path, "ISO", "Android", "Android-Manual-Firmware-Drop", "Samsung", "user-firmware.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(existingAndroidFile)!);
        File.WriteAllText(existingAndroidFile, "user supplied");

        var result = RunUpdate(temp.Path, manifestPath, "macos");

        Assert.Equal(0, result.ExitCode);
        Assert.True(Directory.Exists(Path.Combine(temp.Path, "ISO", "macOS", "macOS-Manual-Installer-Drop", "Sequoia")));
        Assert.True(File.Exists(Path.Combine(temp.Path, "ISO", "macOS", "README - macOS installer workflow.txt")));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "Tools", "Android")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "ISO", "Android", "README - Android firmware workflow.txt")));
        Assert.True(File.Exists(existingAndroidFile));
        Assert.True(File.Exists(Path.Combine(temp.Path, "_docs", "CORE.url")));
    }

    [Fact]
    public void UpdateScript_WhatIfDoesNotSeedProfileExtras()
    {
        using var temp = new TempFolder();
        var manifestPath = WriteProfileTestManifest(temp.Path);

        var result = RunUpdate(temp.Path, manifestPath, "macos", "-WhatIf");

        Assert.Equal(0, result.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "ISO", "macOS")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "_docs", "CORE.url")));
    }

    [Fact]
    public void UpdateScript_ForceControlsReadmeOverwrite()
    {
        using var temp = new TempFolder();
        var manifestPath = WriteProfileTestManifest(temp.Path);
        var readmePath = Path.Combine(temp.Path, "ISO", "macOS", "README - macOS installer workflow.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(readmePath)!);
        File.WriteAllText(readmePath, "keep me");

        Assert.Equal(0, RunUpdate(temp.Path, manifestPath, "macos").ExitCode);
        Assert.Equal("keep me", File.ReadAllText(readmePath));

        Assert.Equal(0, RunUpdate(temp.Path, manifestPath, "macos", "-Force").ExitCode);
        Assert.Contains("ForgerEMS does not redistribute", File.ReadAllText(readmePath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Scripts_ExposeIncludedCategoriesAndDoNotDeleteUncheckedProfileContent()
    {
        var update = File.ReadAllText(Path.Combine(RepoRoot, "backend", "Update-ForgerEMS.ps1"));
        var setup = File.ReadAllText(Path.Combine(RepoRoot, "backend", "Setup_Toolkit.ps1"));

        Assert.Contains("[string[]]$IncludedCategories", update, StringComparison.Ordinal);
        Assert.Contains("[string[]]$IncludedCategories", setup, StringComparison.Ordinal);
        Assert.Contains("existing USB files are not deleted", update, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("existing USB files are not deleted", setup, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement BuilderCategory(string categoryId)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SourceManifestPath));
        return document.RootElement.GetProperty("builderCategories")
            .EnumerateArray()
            .Single(c => string.Equals(GetString(c, "categoryId"), categoryId, StringComparison.OrdinalIgnoreCase))
            .Clone();
    }

    private static JsonElement[] Items()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SourceManifestPath));
        return document.RootElement.GetProperty("items").EnumerateArray().Select(i => i.Clone()).ToArray();
    }

    private static string WriteProfileTestManifest(string root)
    {
        var manifestPath = Path.Combine(root, "profile-test-manifest.json");
        File.WriteAllText(manifestPath, """
{
  "manifestVersion": 1,
  "coreName": "ForgerEMS Ventoy Core",
  "coreVersion": "test",
  "buildTimestampUtc": "2026-05-21T00:00:00Z",
  "releaseType": "dev",
  "managedChecksumPolicy": "warn",
  "settings": {
    "downloadFolder": "_downloads",
    "archiveFolder": "_archive",
    "logFolder": "_logs",
    "timeoutSec": 1,
    "retryCount": 1,
    "userAgent": "ForgerEMS-Test",
    "maxArchivePerItem": 1
  },
  "items": [
    {
      "name": "Core profile marker",
      "type": "page",
      "dest": "_docs\\CORE.url",
      "url": "https://www.ventoy.net/en/download.html",
      "enabled": true,
      "categoryId": "core"
    },
    {
      "name": "macOS profile marker",
      "type": "page",
      "dest": "ISO\\macOS\\GUIDE - Apple macOS download and install guide.url",
      "url": "https://support.apple.com/en-us/120280",
      "enabled": true,
      "categoryId": "macos"
    },
    {
      "name": "Android profile marker",
      "type": "page",
      "dest": "Tools\\Android\\DOWNLOAD - Android Platform Tools adb fastboot.url",
      "url": "https://developer.android.com/tools/releases/platform-tools",
      "enabled": true,
      "categoryId": "android"
    }
  ],
  "extras": {
    "seedDirectories": [
      "ISO\\macOS\\macOS-Manual-Installer-Drop\\Sequoia",
      "Tools\\Android"
    ],
    "readmes": [
      {
        "categoryId": "macos",
        "dest": "ISO\\macOS\\README - macOS installer workflow.txt",
        "body": [
          "user-supplied media",
          "ForgerEMS does not redistribute Apple installers.",
          "Do not use random internet ISO mirrors."
        ]
      },
      {
        "categoryId": "android",
        "dest": "ISO\\Android\\README - Android firmware workflow.txt",
        "body": [
          "user-supplied media",
          "ForgerEMS does not redistribute Android firmware.",
          "Do not use random internet ISO mirrors."
        ]
      }
    ]
  }
}
""");
        return manifestPath;
    }

    private static (int ExitCode, string Output) RunUpdate(string root, string manifestPath, string categories, params string[] extraArgs)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = Path.Combine(RepoRoot, "backend"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(Path.Combine(RepoRoot, "backend", "Update-ForgerEMS.ps1"));
        psi.ArgumentList.Add("-UsbRoot");
        psi.ArgumentList.Add(root);
        psi.ArgumentList.Add("-ManifestName");
        psi.ArgumentList.Add(manifestPath);
        psi.ArgumentList.Add("-IncludedCategories");
        psi.ArgumentList.Add(categories);
        foreach (var arg in extraArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(30000);
        return (process.ExitCode, output);
    }

    private static string ConcatBody(JsonElement readme)
    {
        if (!readme.TryGetProperty("body", out var body))
        {
            return string.Empty;
        }

        return body.ValueKind == JsonValueKind.Array
            ? string.Join('\n', body.EnumerateArray().Select(b => b.GetString() ?? string.Empty))
            : body.GetString() ?? string.Empty;
    }

    private static string GetString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private sealed class TempFolder : IDisposable
    {
        private readonly string _bundleRoot;

        public TempFolder()
        {
            _bundleRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ForgerEMS-ProfileTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_bundleRoot);
            File.WriteAllText(System.IO.Path.Combine(_bundleRoot, "RELEASE-BUNDLE.txt"), "test");
            File.WriteAllText(System.IO.Path.Combine(_bundleRoot, "VERSION.txt"), "test");
            File.WriteAllText(System.IO.Path.Combine(_bundleRoot, "ForgerEMS.updates.json"), "{}");
            Path = System.IO.Path.Combine(_bundleRoot, ".verify", "profile-root");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_bundleRoot))
                {
                    Directory.Delete(_bundleRoot, recursive: true);
                }
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}
