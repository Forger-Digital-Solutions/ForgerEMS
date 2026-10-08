using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ForgerEMS.Wpf.Services.Resources;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>Strict GNU/BSD checksum document parsing.</summary>
public sealed class ChecksumParserTests
{
    private static readonly string ShaA = new string('a', 64);
    private static readonly string ShaB = new string('b', 64);

    [Fact]
    public void GnuStarFormat_Parses()
    {
        var doc = $"{ShaA}  *ventoy-1.1.12-windows.zip\n{ShaB}  other.iso\n";
        var entries = ChecksumParser.Parse(doc);
        Assert.NotNull(entries);
        Assert.Equal(ShaA, ChecksumParser.FindExact(entries!, "ventoy-1.1.12-windows.zip"));
        Assert.Equal(ShaB, ChecksumParser.FindExact(entries!, "other.iso"));
    }

    [Fact]
    public void BsdFormat_Parses()
    {
        var doc = $"SHA256 (ventoy-1.1.12-windows.zip) = {ShaA.ToUpperInvariant()}\n";
        var entries = ChecksumParser.Parse(doc);
        Assert.NotNull(entries);
        Assert.Equal(ShaA, ChecksumParser.FindExact(entries!, "ventoy-1.1.12-windows.zip"));
    }

    [Fact]
    public void WrongFile_LookupReturnsNull()
    {
        var entries = ChecksumParser.Parse($"{ShaA}  real.iso\n");
        Assert.NotNull(entries);
        Assert.Null(ChecksumParser.FindExact(entries!, "real.iso.exe"));
        Assert.Null(ChecksumParser.FindExact(entries!, "REAL.ISO"));
    }

    [Fact]
    public void ConflictingDuplicates_InvalidateDocument()
    {
        var doc = $"{ShaA}  a.iso\n{ShaB}  a.iso\n";
        Assert.Null(ChecksumParser.Parse(doc));
    }

    [Fact]
    public void MalformedLine_InvalidateDocument()
    {
        Assert.Null(ChecksumParser.Parse($"{ShaA}  a.iso\nnot-a-hash line\n"));
    }

    [Fact]
    public void TraversalFileName_Rejected()
    {
        Assert.Null(ChecksumParser.Parse($"{ShaA}  ../evil.iso\n"));
        Assert.Null(ChecksumParser.Parse($"{ShaA}  dir\\evil.iso\n"));
    }
}

/// <summary>resource-policy.json schema validation and bounds.</summary>
public sealed class ResourceCatalogTests
{
    private const string MinimalPolicy = """
        {
          "schemaVersion": 1,
          "defaultChannel": "stable",
          "metadataTimeoutSeconds": 20,
          "maximumAttempts": 2,
          "cacheTtlMinutes": 60,
          "resources": [
            {
              "id": "ventoy",
              "name": "Ventoy Windows Package",
              "provider": "github-stable",
              "repository": "ventoy/Ventoy",
              "source": "https://github.com/ventoy/Ventoy/releases/latest",
              "assetPattern": "^ventoy-[0-9.]+-windows\\.zip$",
              "architecture": "x64",
              "platform": "Windows"
            }
          ]
        }
        """;

    [Fact]
    public void RealPolicyFile_ParsesAndContainsVentoy()
    {
        var repoPolicy = Path.Combine(RepoRoot(), "manifests", "resource-policy.json");
        Assert.True(File.Exists(repoPolicy));
        var catalog = ResourceCatalog.Parse(File.ReadAllText(repoPolicy));
        Assert.NotNull(catalog.Find("ventoy"));
        Assert.Equal(ResourceProviderKind.GitHubStable, catalog.Find("ventoy")!.Provider);
        // Kali must carry the lead-authored explicit redirect allow-list.
        var kali = catalog.Find("kali");
        Assert.NotNull(kali);
        Assert.Contains("kali.download", kali!.AllowedHosts);
        Assert.Contains("cdimage.kali.org", kali.AllowedHosts);
        // Shipped policy: 45s metadata budget (measured serial fetch ~21.7s) and
        // Kali checksum/base URLs on the canonical kali.download host.
        Assert.Equal(TimeSpan.FromSeconds(45), catalog.MetadataTimeout);
        Assert.Equal("https://kali.download/base-images/current/SHA256SUMS", kali.SourceUri.ToString());
        Assert.Equal("https://kali.download/base-images/current/", kali.BaseUrl.ToString());
    }

    [Fact]
    public void MinimalPolicy_ParsesDescriptor()
    {
        var catalog = ResourceCatalog.Parse(MinimalPolicy);
        var d = catalog.Find("ventoy");
        Assert.NotNull(d);
        Assert.Equal("ventoy/Ventoy", d!.Repository);
        Assert.Equal("stable", catalog.DefaultChannel);
    }

    [Fact]
    public void WrongSchemaVersion_Throws()
    {
        var bad = MinimalPolicy.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99");
        Assert.Throws<ResourceCatalogException>(() => ResourceCatalog.Parse(bad));
    }

    [Fact]
    public void DuplicateResourceId_Throws()
    {
        var dup = System.Text.RegularExpressions.Regex.Replace(
            MinimalPolicy,
            "\\}\\s*\\]\\s*\\}\\s*$",
            "},{ \"id\": \"ventoy\", \"name\": \"Dup\", \"provider\": \"github-stable\", \"repository\": \"a/b\", " +
            "\"source\": \"https://github.com/a/b/releases/latest\", \"architecture\": \"x64\" }] }");
        Assert.Throws<ResourceCatalogException>(() => ResourceCatalog.Parse(dup));
    }

    [Fact]
    public void BadNumericField_ThrowsTypedException()
    {
        var bad = MinimalPolicy.Replace("\"metadataTimeoutSeconds\": 20", "\"metadataTimeoutSeconds\": \"soon\"");
        Assert.Throws<ResourceCatalogException>(() => ResourceCatalog.Parse(bad));
    }

    [Fact]
    public void MalformedJson_Throws()
    {
        Assert.Throws<ResourceCatalogException>(() => ResourceCatalog.Parse("{not json"));
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ForgerEMS.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}

/// <summary>Provider resolution with mock transport — no real network.</summary>
public sealed class ResourceProviderResolverTests
{
    private static readonly string ShaZ = new string('f', 64);

    private static ResourceDescriptor VentoyDescriptor() => new()
    {
        ResourceId = "ventoy",
        DisplayName = "Ventoy",
        Provider = ResourceProviderKind.GitHubStable,
        Repository = "ventoy/Ventoy",
        SourceUri = "https://github.com/ventoy/Ventoy/releases/latest",
        AssetPattern = "^ventoy-[0-9.]+-windows\\.zip$",
        Architecture = "x64"
    };

    private static string ReleaseJson(
        string tag = "v1.1.12",
        bool draft = false,
        bool prerelease = false,
        string? digest = null,
        string assetName = "ventoy-1.1.12-windows.zip",
        string? assetUrl = null)
    {
        var digestPart = digest is null ? "" : $",\"digest\":\"sha256:{digest}\"";
        var url = assetUrl
            ?? $"https://github.com/ventoy/Ventoy/releases/download/{tag}/{assetName}";
        return $$"""
            {
              "tag_name": "{{tag}}",
              "draft": {{draft.ToString().ToLowerInvariant()}},
              "prerelease": {{prerelease.ToString().ToLowerInvariant()}},
              "published_at": "2026-01-01T00:00:00Z",
              "assets": [
                {"name": "{{assetName}}", "browser_download_url": "{{url}}", "size": 12345{{digestPart}}}
              ]
            }
            """;
    }

    private static ResourceProviderResolver Resolver(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var client = new HttpClient(new DelegateHandler(respond));
        return new ResourceProviderResolver(new OfficialMetadataClient(client), maxAttempts: 1);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task GitHubStable_ValidDigest_Resolves()
    {
        var resolver = Resolver(_ => Json(ReleaseJson(digest: ShaZ)));
        var result = await resolver.ResolveAsync(VentoyDescriptor(), CancellationToken.None);
        Assert.Equal(ResourceResolutionState.ResolvedMetadata, result.State);
        Assert.Equal(ShaZ, result.ExpectedSha256);
        Assert.True(result.ExpiresAtUtc is null); // expiry stamped by check service, not provider
        Assert.Equal("ventoy-1.1.12-windows.zip", result.ArtifactFileName);
    }

    [Fact]
    public async Task GitHubStable_MalformedDigest_RejectedAsNoHash()
    {
        var resolver = Resolver(_ => Json(ReleaseJson(digest: "not-hex!")));
        var result = await resolver.ResolveAsync(VentoyDescriptor(), CancellationToken.None);
        Assert.Equal(ResourceFailureCategory.NoExpectedHash, result.FailureCategory);
    }

    [Fact]
    public async Task GitHubStable_Draft_Rejected()
    {
        var resolver = Resolver(_ => Json(ReleaseJson(draft: true, digest: ShaZ)));
        var result = await resolver.ResolveAsync(VentoyDescriptor(), CancellationToken.None);
        Assert.Equal(ResourceResolutionState.RequiresUserAction, result.State);
    }

    [Fact]
    public async Task GitHubStable_MissingOrWrongKindDraftFlag_FailsClosed()
    {
        var missingFlags = """
            {
              "tag_name": "v1.1.12",
              "assets": [
                {"name": "ventoy-1.1.12-windows.zip", "browser_download_url": "https://github.com/ventoy/Ventoy/releases/download/v1.1.12/ventoy-1.1.12-windows.zip", "size": 12345}
              ]
            }
            """;
        var wrongKind = missingFlags.Replace("\"tag_name\": \"v1.1.12\",",
            "\"tag_name\": \"v1.1.12\", \"draft\": \"false\", \"prerelease\": false,");

        foreach (var body in new[] { missingFlags, wrongKind })
        {
            var resolver = Resolver(_ => Json(body));
            var result = await resolver.ResolveAsync(VentoyDescriptor(), CancellationToken.None);
            Assert.Equal(ResourceResolutionState.UnableToVerify, result.State);
            Assert.Equal(ResourceFailureCategory.MetadataInvalid, result.FailureCategory);
        }
    }

    [Fact]
    public async Task GitHubStable_Prerelease_Rejected()
    {
        var resolver = Resolver(_ => Json(ReleaseJson(prerelease: true, digest: ShaZ)));
        var result = await resolver.ResolveAsync(VentoyDescriptor(), CancellationToken.None);
        Assert.Equal(ResourceResolutionState.RequiresUserAction, result.State);
    }

    [Fact]
    public async Task GitHubStable_BadJson_FailsCleanly()
    {
        var resolver = Resolver(_ => Json("{broken"));
        var result = await resolver.ResolveAsync(VentoyDescriptor(), CancellationToken.None);
        Assert.Equal(ResourceFailureCategory.MetadataInvalid, result.FailureCategory);
    }

    [Fact]
    public async Task GitHubStable_EmptyObject_FailsCleanly()
    {
        var resolver = Resolver(_ => Json("{}"));
        var result = await resolver.ResolveAsync(VentoyDescriptor(), CancellationToken.None);
        Assert.Equal(ResourceFailureCategory.MetadataInvalid, result.FailureCategory);
    }

    [Fact]
    public async Task GitHubStable_UntrustedAssetHost_Rejected()
    {
        var resolver = Resolver(_ => Json(ReleaseJson(
            digest: ShaZ, assetUrl: "https://evil-cdn.example.test/ventoy-1.1.12-windows.zip")));
        var result = await resolver.ResolveAsync(VentoyDescriptor(), CancellationToken.None);
        Assert.Equal(ResourceResolutionState.UnableToVerify, result.State);
    }

    [Fact]
    public async Task GitHubStable_AmbiguousAssets_NotFirstWins()
    {
        var json = """
            {
              "tag_name": "v1.1.12",
              "draft": false,
              "prerelease": false,
              "assets": [
                {"name": "ventoy-1.1.12-windows.zip", "browser_download_url": "https://github.com/ventoy/Ventoy/releases/download/v1.1.12/ventoy-1.1.12-windows.zip", "digest": "sha256:aaaa"},
                {"name": "ventoy-1.1.12-windows.zip.bak", "browser_download_url": "https://github.com/ventoy/Ventoy/releases/download/v1.1.12/ventoy-1.1.12-windows.zip.bak", "digest": "sha256:bbbb"}
              ]
            }
            """;
        // assetPattern allows any file after "-windows.zip"? Use a pattern matching both.
        var descriptor = VentoyDescriptor() with { AssetPattern = "^ventoy-[0-9.]+-windows\\.zip.*$" };
        var resolver = Resolver(_ => Json(json));
        var result = await resolver.ResolveAsync(descriptor, CancellationToken.None);
        Assert.Equal(ResourceFailureCategory.AmbiguousSelection, result.FailureCategory);
    }

    [Fact]
    public async Task GitHubStable_UpstreamDown_Fails()
    {
        var resolver = Resolver(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var result = await resolver.ResolveAsync(VentoyDescriptor(), CancellationToken.None);
        Assert.Equal(ResourceResolutionState.UnableToVerify, result.State);
    }

    [Fact]
    public async Task UbuntuLts_SupportedCurrentRelease_Resolves()
    {
        var descriptor = new ResourceDescriptor
        {
            ResourceId = "ubuntu-desktop",
            DisplayName = "Ubuntu LTS Desktop",
            Provider = ResourceProviderKind.UbuntuLts,
            SourceUri = "https://changelogs.ubuntu.com/meta-release-lts",
            AssetPattern = "^ubuntu-[0-9.]+-desktop-amd64\\.iso$",
            Architecture = "x64",
            Channel = "lts"
        };

        var metaRelease = """
            Dist: noble
            Name: Noble Numbat
            Version: 24.04.3 LTS
            Date: Thu, 15 Aug 2025 12:00:00 UTC
            Supported: 1
            LTS: true

            Dist: resolute
            Name: Resolute
            Version: 26.04
            Date: Thu, 15 Aug 2026 12:00:00 UTC
            Supported: 0

            """;
        var shaIso = new string('e', 64);
        var sums = $"{shaIso} *ubuntu-24.04.3-desktop-amd64.iso\n{shaIso} *ubuntu-24.04.3-live-server-amd64.iso\n";

        var resolver = Resolver(req =>
        {
            var host = req.RequestUri!.Host;
            if (host.Equals("changelogs.ubuntu.com", StringComparison.OrdinalIgnoreCase))
            {
                return Json(metaRelease);
            }

            if (host.Equals("releases.ubuntu.com", StringComparison.OrdinalIgnoreCase))
            {
                return Json(sums);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var result = await resolver.ResolveAsync(descriptor, CancellationToken.None);
        Assert.Equal(ResourceResolutionState.ResolvedMetadata, result.State);
        Assert.Equal("24.04.3", result.Version);
        Assert.Equal(shaIso, result.ExpectedSha256);
        Assert.Contains("releases.ubuntu.com/noble/", result.ArtifactUri);
    }

    [Fact]
    public async Task UbuntuLts_NewerLtsRoleSelectedOverOlder()
    {
        var descriptor = new ResourceDescriptor
        {
            ResourceId = "ubuntu-desktop",
            DisplayName = "Ubuntu LTS Desktop",
            Provider = ResourceProviderKind.UbuntuLts,
            SourceUri = "https://changelogs.ubuntu.com/meta-release-lts",
            AssetPattern = "^ubuntu-[0-9.]+-desktop-amd64\\.iso$",
            Architecture = "x64",
            Channel = "lts"
        };

        var metaRelease = """
            Dist: jammy
            Name: Jammy Jellyfish
            Version: 22.04.5 LTS
            Supported: 1
            LTS: true

            Dist: noble
            Name: Noble Numbat
            Version: 24.04.3 LTS
            Supported: 1
            LTS: true

            """;
        var shaIso = new string('d', 64);
        var resolver = Resolver(req =>
        {
            var host = req.RequestUri!.Host;
            if (host.Equals("changelogs.ubuntu.com", StringComparison.OrdinalIgnoreCase))
            {
                return Json(metaRelease);
            }

            if (host.Equals("releases.ubuntu.com", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains("/noble/", req.RequestUri.AbsolutePath);
                return Json($"{shaIso} *ubuntu-24.04.3-desktop-amd64.iso\n");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var result = await resolver.ResolveAsync(descriptor, CancellationToken.None);
        Assert.Equal("24.04.3", result.Version);
        Assert.Contains("/noble/", result.ArtifactUri);
    }

    [Fact]
    public async Task ChecksumIndex_KaliRedirectAllowedByDescriptorHosts()
    {
        var descriptor = new ResourceDescriptor
        {
            ResourceId = "kali",
            DisplayName = "Kali Linux Installer",
            Provider = ResourceProviderKind.ChecksumIndex,
            SourceUri = "https://cdimage.kali.org/current/SHA256SUMS",
            BaseUrl = "https://cdimage.kali.org/current/",
            AssetPattern = "^kali-linux-[0-9.]+-installer-amd64\\.iso$",
            Architecture = "x64",
            AllowedHosts = new[] { "cdimage.kali.org", "kali.download" }
        };

        var shaIso = new string('c', 64);
        var resolver = Resolver(req =>
        {
            var host = req.RequestUri!.Host;
            if (host.Equals("cdimage.kali.org", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://kali.download/base-images/kali-2026.2/SHA256SUMS") }
                };
            }

            if (host.Equals("kali.download", StringComparison.OrdinalIgnoreCase))
            {
                return Json($"{shaIso} *kali-linux-2026.2-installer-amd64.iso\n");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var result = await resolver.ResolveAsync(descriptor, CancellationToken.None);
        Assert.Equal(ResourceResolutionState.ResolvedMetadata, result.State);
        Assert.Equal(shaIso, result.ExpectedSha256);
        Assert.Equal("kali-linux-2026.2-installer-amd64.iso", result.ArtifactFileName);
        // The artifact URL is built from the descriptor's canonical base; the redirect host is
        // only followed for metadata fetches because the descriptor explicitly allows it.
        Assert.StartsWith("https://cdimage.kali.org/", result.ArtifactUri);
    }

    [Fact]
    public async Task ChecksumIndex_UnlistedRedirectHost_Rejected()
    {
        var descriptor = new ResourceDescriptor
        {
            ResourceId = "kali",
            DisplayName = "Kali Linux Installer",
            Provider = ResourceProviderKind.ChecksumIndex,
            SourceUri = "https://cdimage.kali.org/current/SHA256SUMS",
            BaseUrl = "https://cdimage.kali.org/current/",
            AssetPattern = "^kali-linux-[0-9.]+-installer-amd64\\.iso$",
            Architecture = "x64",
            AllowedHosts = new[] { "cdimage.kali.org" } // kali.download deliberately absent
        };

        var resolver = Resolver(req =>
        {
            if (req.RequestUri!.Host.Equals("cdimage.kali.org", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://kali.download/base-images/kali-2026.2/SHA256SUMS") }
                };
            }

            return Json("unreachable");
        });

        var result = await resolver.ResolveAsync(descriptor, CancellationToken.None);
        Assert.NotEqual(ResourceResolutionState.ResolvedMetadata, result.State);
    }

    [Fact]
    public async Task OfficialPage_AlwaysRequiresUserAction()
    {
        var descriptor = new ResourceDescriptor
        {
            ResourceId = "memtest86plus",
            DisplayName = "MemTest86+",
            Provider = ResourceProviderKind.OfficialPage,
            SourceUri = "https://www.memtest.org/",
            Architecture = "x64",
            ExceptionReason = "Vendor adapter unimplemented."
        };
        var resolver = Resolver(_ => throw new InvalidOperationException("no network expected"));
        var result = await resolver.ResolveAsync(descriptor, CancellationToken.None);
        Assert.Equal(ResourceResolutionState.RequiresUserAction, result.State);
        Assert.False(result.IsDownloadEligible);
    }

    // ---------- github two-part numeric tag fallback (resource providers only) ----------

    private static ResourceDescriptor PolicyDescriptor(string id)
    {
        var catalog = ResourceCatalog.Parse(File.ReadAllText(
            Path.Combine(ResourceCatalogTests.RepoRoot(), "manifests", "resource-policy.json")));
        return catalog.Find(id)!;
    }

    [Theory]
    [InlineData("v4.15")]
    [InlineData("4.15")]
    public async Task GitHubStable_TwoPartNumericTag_ResolvesWithVendorLabel(string tag)
    {
        var descriptor = PolicyDescriptor("rufus"); // pattern ^rufus-[0-9.]+p\.exe$
        var url = $"https://github.com/pbatard/rufus/releases/download/{tag}/rufus-4.15p.exe";
        var resolver = Resolver(_ => Json(ReleaseJson(
            tag: tag, digest: ShaZ, assetName: "rufus-4.15p.exe", assetUrl: url)));
        var result = await resolver.ResolveAsync(descriptor, CancellationToken.None);
        Assert.Equal(ResourceResolutionState.ResolvedMetadata, result.State);
        Assert.Equal("4.15", result.Version);
        Assert.Equal(ShaZ, result.ExpectedSha256);
        Assert.Equal(url, result.ArtifactUri);
    }

    [Fact]
    public void AppSemanticVersion_StillRejectsTwoPartTag()
    {
        // The app updater parser stays strict; the two-part fallback exists only inside
        // the resource provider, not in shared version parsing.
        Assert.False(VentoyToolkitSetup.Wpf.Services.AppSemanticVersion.TryParse("v4.15", out _));
        Assert.False(VentoyToolkitSetup.Wpf.Services.AppSemanticVersion.TryParse("4.15", out _));
    }

    [Theory]
    [InlineData("v4.15-beta")]
    [InlineData("v4.15.2-extra")]
    [InlineData("2026-10-07")]
    [InlineData("nightly")]
    public async Task GitHubStable_NonWholeNumericTag_Rejected(string tag)
    {
        var url = $"https://github.com/pbatard/rufus/releases/download/{tag}/rufus-4.15p.exe";
        var resolver = Resolver(_ => Json(ReleaseJson(
            tag: tag, digest: ShaZ, assetName: "rufus-4.15p.exe", assetUrl: url)));
        var result = await resolver.ResolveAsync(PolicyDescriptor("rufus"), CancellationToken.None);
        Assert.Equal(ResourceResolutionState.UnableToVerify, result.State);
        Assert.Equal(ResourceFailureCategory.MetadataInvalid, result.FailureCategory);
    }

    // ---------- ubuntu-lts filename-version selection ----------

    private static ResourceDescriptor UbuntuDescriptor() => new()
    {
        ResourceId = "ubuntu-desktop",
        DisplayName = "Ubuntu LTS Desktop",
        Provider = ResourceProviderKind.UbuntuLts,
        SourceUri = "https://changelogs.ubuntu.com/meta-release-lts",
        AssetPattern = "^ubuntu-[0-9.]+-desktop-amd64\\.iso$",
        Architecture = "x64",
        Channel = "lts"
    };

    private const string NobleMetaRelease = """
        Dist: noble
        Name: Noble Numbat
        Version: 24.04.3 LTS
        Date: Thu, 15 Aug 2025 12:00:00 UTC
        Supported: 1
        LTS: true

        Dist: resolute
        Name: Resolute
        Version: 26.04
        Date: Thu, 15 Aug 2026 12:00:00 UTC
        Supported: 0

        """;

    private static ResourceProviderResolver UbuntuResolver(string sums) => Resolver(req =>
    {
        var host = req.RequestUri!.Host;
        if (host.Equals("changelogs.ubuntu.com", StringComparison.OrdinalIgnoreCase))
        {
            return Json(NobleMetaRelease);
        }

        if (host.Equals("releases.ubuntu.com", StringComparison.OrdinalIgnoreCase))
        {
            return Json(sums);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    });

    [Fact]
    public async Task UbuntuLts_NewestPointReleaseWins()
    {
        var shaOld = new string('a', 64);
        var shaNew = new string('b', 64);
        var sums = $"{shaOld} *ubuntu-24.04.3-desktop-amd64.iso\n{shaNew} *ubuntu-24.04.4-desktop-amd64.iso\n";
        var result = await UbuntuResolver(sums).ResolveAsync(UbuntuDescriptor(), CancellationToken.None);
        Assert.Equal(ResourceResolutionState.ResolvedMetadata, result.State);
        Assert.Equal("24.04.4", result.Version);
        Assert.Equal(shaNew, result.ExpectedSha256);
        Assert.Equal("ubuntu-24.04.4-desktop-amd64.iso", result.ArtifactFileName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UbuntuLts_FourPartVersionBeatsThreePart_RegardlessOfOrder(bool fourPartFirst)
    {
        var sha5 = new string('c', 64);
        var sha51 = new string('d', 64);
        var sums = fourPartFirst
            ? $"{sha51} *ubuntu-24.04.5.1-desktop-amd64.iso\n{sha5} *ubuntu-24.04.5-desktop-amd64.iso\n"
            : $"{sha5} *ubuntu-24.04.5-desktop-amd64.iso\n{sha51} *ubuntu-24.04.5.1-desktop-amd64.iso\n";
        var result = await UbuntuResolver(sums).ResolveAsync(UbuntuDescriptor(), CancellationToken.None);
        Assert.Equal("24.04.5.1", result.Version);
        Assert.Equal(sha51, result.ExpectedSha256);
    }

    [Fact]
    public async Task UbuntuLts_EqualNormalizedVersions_AreAmbiguous()
    {
        var sums = $"{new string('e', 64)} *ubuntu-24.04.5-desktop-amd64.iso\n" +
            $"{new string('f', 64)} *ubuntu-24.04.05-desktop-amd64.iso\n";
        var result = await UbuntuResolver(sums).ResolveAsync(UbuntuDescriptor(), CancellationToken.None);
        Assert.Equal(ResourceResolutionState.RequiresUserAction, result.State);
        Assert.Equal(ResourceFailureCategory.AmbiguousSelection, result.FailureCategory);
    }

    [Fact]
    public async Task UbuntuLts_ExplicitZeroRevision_Ambiguous()
    {
        // 24.04.5 and 24.04.5.0 normalize to the same four-part version -> no first/last wins.
        var sums = $"{new string('e', 64)} *ubuntu-24.04.5-desktop-amd64.iso\n" +
            $"{new string('f', 64)} *ubuntu-24.04.5.0-desktop-amd64.iso\n";
        var result = await UbuntuResolver(sums).ResolveAsync(UbuntuDescriptor(), CancellationToken.None);
        Assert.Equal(ResourceResolutionState.RequiresUserAction, result.State);
        Assert.Equal(ResourceFailureCategory.AmbiguousSelection, result.FailureCategory);
    }

    [Fact]
    public async Task UbuntuLts_ImageOlderThanStanza_Rejected()
    {
        // Metadata declares 24.04.3 LTS supported, but the index only offers 24.04.2.
        var sums = $"{new string('a', 64)} *ubuntu-24.04.2-desktop-amd64.iso\n";
        var result = await UbuntuResolver(sums).ResolveAsync(UbuntuDescriptor(), CancellationToken.None);
        Assert.Equal(ResourceResolutionState.UnableToVerify, result.State);
        Assert.Equal(ResourceFailureCategory.MetadataInvalid, result.FailureCategory);
    }

    [Fact]
    public async Task UbuntuLts_ForeignFamilyEntry_FailsMetadataInvalid()
    {
        var sums = $"{new string('a', 64)} *ubuntu-24.04.4-desktop-amd64.iso\n" +
            $"{new string('b', 64)} *ubuntu-26.04-desktop-amd64.iso\n";
        var result = await UbuntuResolver(sums).ResolveAsync(UbuntuDescriptor(), CancellationToken.None);
        Assert.Equal(ResourceResolutionState.UnableToVerify, result.State);
        Assert.Equal(ResourceFailureCategory.MetadataInvalid, result.FailureCategory);
    }

    [Fact]
    public async Task UbuntuLts_OnlyOtherRoleEntries_FailsMetadataInvalid()
    {
        var sums = $"{new string('a', 64)} *ubuntu-24.04.4-live-server-amd64.iso\n";
        var result = await UbuntuResolver(sums).ResolveAsync(UbuntuDescriptor(), CancellationToken.None);
        Assert.Equal(ResourceResolutionState.UnableToVerify, result.State);
        Assert.Equal(ResourceFailureCategory.MetadataInvalid, result.FailureCategory);
    }

    [Fact]
    public async Task UbuntuLts_ConflictingHashes_Rejected()
    {
        var sums = $"{new string('a', 64)} *ubuntu-24.04.4-desktop-amd64.iso\n" +
            $"{new string('b', 64)} *ubuntu-24.04.4-desktop-amd64.iso\n";
        var result = await UbuntuResolver(sums).ResolveAsync(UbuntuDescriptor(), CancellationToken.None);
        Assert.Equal(ResourceResolutionState.UnableToVerify, result.State);
        Assert.Equal(ResourceFailureCategory.MetadataInvalid, result.FailureCategory);
    }

    // ---------- systeminformer policy fixture ----------

    private static ResourceDescriptor SystemInformerDescriptor() => PolicyDescriptor("systeminformer");

    private static string SystemInformerReleaseJson(params (string Name, string Digest)[] assets)
    {
        var assetJson = string.Join(", ", assets.Select(a =>
            $"{{\"name\": \"{a.Name}\", \"browser_download_url\": " +
            $"\"https://github.com/winsiderss/systeminformer/releases/download/v4.0.26241.138/{a.Name}\", " +
            $"\"digest\": \"sha256:{a.Digest}\", \"size\": 12345}}"));
        return $$"""
            {
              "tag_name": "v4.0.26241.138",
              "draft": false,
              "prerelease": false,
              "published_at": "2026-01-01T00:00:00Z",
              "assets": [{{assetJson}}]
            }
            """;
    }

    [Fact]
    public async Task SystemInformerPolicy_CurrentBinZip_Resolves()
    {
        var descriptor = SystemInformerDescriptor();
        var resolver = Resolver(_ => Json(SystemInformerReleaseJson(
            ("systeminformer-4.0.26241.138-bin.zip", ShaZ))));
        var result = await resolver.ResolveAsync(descriptor, CancellationToken.None);
        Assert.Equal(ResourceResolutionState.ResolvedMetadata, result.State);
        Assert.Equal("4.0.26241.138", result.Version);
        Assert.Equal("systeminformer-4.0.26241.138-bin.zip", result.ArtifactFileName);
        Assert.Equal(ShaZ, result.ExpectedSha256);
    }

    [Theory]
    [InlineData("systeminformer-4.0.26241.138-src.zip")]
    [InlineData("systeminformer-4.0.26241.138-bin-x86.zip")]
    [InlineData("systeminformer-4.0.26241.138-bin.zip.bak")]
    [InlineData("systeminformer-4.0.26241.138-setup.exe")]
    public async Task SystemInformerPolicy_NonBinAssets_NotSelected(string assetName)
    {
        var descriptor = SystemInformerDescriptor();
        var resolver = Resolver(_ => Json(SystemInformerReleaseJson((assetName, ShaZ))));
        var result = await resolver.ResolveAsync(descriptor, CancellationToken.None);
        Assert.Equal(ResourceResolutionState.UnableToVerify, result.State);
        Assert.Equal(ResourceFailureCategory.MetadataInvalid, result.FailureCategory);
    }

    [Fact]
    public async Task SystemInformerPolicy_OldAndNewLayouts_Ambiguous()
    {
        var descriptor = SystemInformerDescriptor();
        var resolver = Resolver(_ => Json(SystemInformerReleaseJson(
            ("systeminformer-4.0.26241.138-bin.zip", ShaZ),
            ("systeminformer-4.0.26241.138-release-bin.zip", new string('a', 64)))));
        var result = await resolver.ResolveAsync(descriptor, CancellationToken.None);
        Assert.Equal(ResourceResolutionState.RequiresUserAction, result.State);
        Assert.Equal(ResourceFailureCategory.AmbiguousSelection, result.FailureCategory);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}

/// <summary>TTL metadata cache trust/chronology validation.</summary>
public sealed class ResourceMetadataCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "forgerems-cache-" + Guid.NewGuid().ToString("N"));

    private static ResourceDescriptor Descriptor() => new()
    {
        ResourceId = "ventoy",
        DisplayName = "Ventoy",
        Provider = ResourceProviderKind.GitHubStable,
        Repository = "ventoy/Ventoy",
        SourceUri = "https://github.com/ventoy/Ventoy/releases/latest",
        Architecture = "x64",
        AllowedHosts = new[] { "github.com" }
    };

    private static ResourceResolution Fresh(ResourceDescriptor d) => new()
    {
        Descriptor = d,
        State = ResourceResolutionState.ResolvedMetadata,
        Version = "1.1.12",
        ArtifactUri = "https://github.com/ventoy/Ventoy/releases/download/v1.1.12/ventoy-1.1.12-windows.zip",
        ArtifactFileName = "ventoy-1.1.12-windows.zip",
        ExpectedSha256 = new string('a', 64),
        CheckedAtUtc = DateTimeOffset.UtcNow,
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(60)
    };

    [Fact]
    public void FreshEntry_ReturnedAsFresh()
    {
        var cache = new ResourceMetadataCache(_dir);
        var descriptor = Descriptor();
        cache.Store(Fresh(descriptor), TimeSpan.FromMinutes(60));
        Assert.True(cache.TryGet(descriptor, TimeSpan.FromMinutes(60), out var res, out var fresh));
        Assert.True(fresh);
        Assert.Equal(ResourceResolutionState.ResolvedMetadata, res!.State);
        Assert.Equal("1.1.12", res.Version);
    }

    [Fact]
    public void StaleEntry_ReturnedAsStale_NotDownloadEligible()
    {
        var cache = new ResourceMetadataCache(_dir);
        var descriptor = Descriptor();
        var stale = Fresh(descriptor) with
        {
            CheckedAtUtc = DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
        };
        cache.Store(stale, TimeSpan.FromMinutes(60));
        Assert.True(cache.TryGet(descriptor, TimeSpan.FromMinutes(60), out var res, out var fresh));
        Assert.False(fresh);
        Assert.Equal(ResourceResolutionState.CachedStale, res!.State);
        Assert.False(res.IsDownloadEligible);
    }

    [Fact]
    public void Fingerprint_SourceKeyMismatch_Misses()
    {
        var cache = new ResourceMetadataCache(_dir);
        var descriptor = Descriptor();
        cache.Store(Fresh(descriptor), TimeSpan.FromMinutes(60));
        var other = descriptor with { Repository = "other/repo" };
        Assert.False(cache.TryGet(other, TimeSpan.FromMinutes(60), out _, out _));
    }

    [Fact]
    public void CachedUntrustedHost_Rejected()
    {
        var cache = new ResourceMetadataCache(_dir);
        var descriptor = Descriptor();
        cache.Store(
            Fresh(descriptor) with { ArtifactUri = "https://evil.example.test/payload.zip" },
            TimeSpan.FromMinutes(60));
        Assert.False(cache.TryGet(descriptor, TimeSpan.FromMinutes(60), out _, out _));
    }

    [Fact]
    public void CachedBadSha_Rejected()
    {
        var cache = new ResourceMetadataCache(_dir);
        var descriptor = Descriptor();
        cache.Store(Fresh(descriptor) with { ExpectedSha256 = "zzz" }, TimeSpan.FromMinutes(60));
        Assert.False(cache.TryGet(descriptor, TimeSpan.FromMinutes(60), out _, out _));
    }

    private static ResourceDescriptor PuttyDescriptor() => new()
    {
        ResourceId = "putty",
        DisplayName = "PuTTY 64-bit Installer",
        Provider = ResourceProviderKind.ChecksumIndex,
        SourceUri = "https://the.earth.li/~sgtatham/putty/latest/sha256sums",
        BaseUrl = "https://the.earth.li/~sgtatham/putty/latest/",
        AssetPattern = "^w64/putty-64bit-[0-9.]+-installer\\.msi$",
        Architecture = "x64",
        AllowedHosts = new[] { "the.earth.li" }
    };

    [Fact]
    public void CachedSubdirectoryPattern_MatchesRelativePathUnderBaseUrl()
    {
        var cache = new ResourceMetadataCache(_dir);
        var descriptor = PuttyDescriptor();
        cache.Store(
            Fresh(descriptor) with
            {
                ArtifactUri = "https://the.earth.li/~sgtatham/putty/latest/w64/putty-64bit-0.81-installer.msi",
                ArtifactFileName = "putty-64bit-0.81-installer.msi"
            },
            TimeSpan.FromMinutes(60));
        Assert.True(cache.TryGet(descriptor, TimeSpan.FromMinutes(60), out var res, out var fresh));
        Assert.True(fresh);
        Assert.Equal(ResourceResolutionState.ResolvedMetadata, res!.State);
    }

    [Fact]
    public void CachedSubdirectoryPattern_RejectsWrongSubdirAndOutsideBaseRoot()
    {
        var cache = new ResourceMetadataCache(_dir);
        var descriptor = PuttyDescriptor();
        foreach (var uri in new[]
        {
            "https://the.earth.li/~sgtatham/putty/latest/x32/putty-64bit-0.81-installer.msi",
            "https://the.earth.li/~sgtatham/putty/w64/putty-64bit-0.81-installer.msi"
        })
        {
            cache.Store(
                Fresh(descriptor) with
                {
                    ArtifactUri = uri,
                    ArtifactFileName = "putty-64bit-0.81-installer.msi"
                },
                TimeSpan.FromMinutes(60));
            Assert.False(cache.TryGet(descriptor, TimeSpan.FromMinutes(60), out _, out _), uri);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
