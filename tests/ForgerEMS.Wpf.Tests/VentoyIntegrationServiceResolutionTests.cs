using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ForgerEMS.Wpf.Services.Resources;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Ventoy resolution goes through the shared resource policy + check service; there is no
/// pinned manifest fallback and no launch except through the captured process seam.
/// </summary>
public sealed class VentoyIntegrationServiceResolutionTests
{
    private static readonly byte[] ZipBytes = BuildVentoyZip();
    private static readonly string ZipSha = Convert.ToHexString(SHA256.HashData(ZipBytes)).ToLowerInvariant();

    private const string PolicyJson = """
        {
          "schemaVersion": 1,
          "defaultChannel": "stable",
          "metadataTimeoutSeconds": 20,
          "maximumAttempts": 1,
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

    private static byte[] BuildVentoyZip()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("ventoy-1.1.12/Ventoy2Disk.exe");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("fixture");
        }

        return ms.ToArray();
    }

    private static string ReleaseJson() => $$"""
        {
          "tag_name": "v1.1.12",
          "draft": false,
          "prerelease": false,
          "published_at": "2026-01-01T00:00:00Z",
          "assets": [
            {
              "name": "ventoy-1.1.12-windows.zip",
              "browser_download_url": "https://github.com/ventoy/Ventoy/releases/download/v1.1.12/ventoy-1.1.12-windows.zip",
              "digest": "sha256:{{ZipSha}}",
              "size": {{ZipBytes.Length}}
            }
          ]
        }
        """;

    private static ResourceCheckService BuildCheckService(
        string cacheDir,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        var catalog = ResourceCatalog.Parse(PolicyJson);
        var transport = new OfficialMetadataClient(new HttpClient(new DelegateHandler(respond)));
        return new ResourceCheckService(
            catalog,
            new ResourceProviderResolver(transport, maxAttempts: 1),
            new ResourceMetadataCache(cacheDir));
    }

    private static VentoyIntegrationService BuildService(
        string root,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        out ResourceCheckService checkService)
    {
        checkService = BuildCheckService(Path.Combine(root, "cache"), respond);
        var artifactDownloads = new ArtifactDownloadService(
            new OfficialMetadataClient(new HttpClient(new DelegateHandler(respond))));
        return new VentoyIntegrationService(
            new NoopPowerShellRunner(),
            new AppRuntimeService(),
            checkService: checkService,
            artifactDownloads: artifactDownloads);
    }

    private static Task<HttpResponseMessage> ServeLatest(
        HttpRequestMessage request, CancellationToken _)
    {
        if (request.RequestUri!.Host == "api.github.com")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ReleaseJson(), Encoding.UTF8, "application/json")
            });
        }

        if (request.RequestUri!.Host == "github.com")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(ZipBytes)
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task GetStatusAsync_ResolvesOfficialMetadataViaPolicy()
    {
        using var root = new TempDir();
        var service = BuildService(root.Path, ServeLatest, out _);
        var status = await service.GetStatusAsync(BuildContext(root.Path), null);
        Assert.True(status.PackageAvailable);
        Assert.Contains("1.1.12", status.PackageText, StringComparison.Ordinal);
        // Metadata stage binds an EXPECTED hash; bytes have not been verified yet.
        Assert.Contains("expected SHA-256", status.PackageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStatusAsync_Unresolved_NoPinnedFallback()
    {
        using var root = new TempDir();
        var service = BuildService(root.Path, (_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)), out _);
        var status = await service.GetStatusAsync(BuildContext(root.Path), null);
        Assert.False(status.PackageAvailable);
        Assert.Contains("could not be resolved", status.PackageText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pinned", status.PackageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_VerifiedDownload_SafeExtract_CapturedLaunch()
    {
        using var root = new TempDir();
        var service = BuildService(root.Path, ServeLatest, out _);
        ProcessStartInfo? launched = null;
        service.ProcessStarter = info =>
        {
            launched = info;
            return null;
        };

        var target = new UsbTargetInfo { RootPath = root.Path };
        var result = await service.InstallOrUpdateAsync(BuildContext(root.Path), target);
        Assert.True(result.Succeeded, result.Details);
        Assert.NotNull(launched);
        Assert.EndsWith("Ventoy2Disk.exe", launched!.FileName);
    }

    [Fact]
    public async Task InstallOrUpdateAsync_UnresolvedSource_FailsClosed()
    {
        using var root = new TempDir();
        var service = BuildService(root.Path, (_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)), out _);
        var target = new UsbTargetInfo { RootPath = root.Path };
        var result = await service.InstallOrUpdateAsync(BuildContext(root.Path), target);
        Assert.False(result.Succeeded);
        Assert.Contains("unavailable", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    private static BackendContext BuildContext(string root) => new()
    {
        IsAvailable = true,
        Mode = BackendMode.Repo,
        RootPath = root,
        WorkingDirectory = root
    };

    private sealed class NoopPowerShellRunner : IPowerShellRunnerService
    {
        public Task<PowerShellRunResult> RunAsync(
            PowerShellRunRequest request,
            Action<LogLine>? onOutput = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PowerShellRunResult { ExitCode = 0 });
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            sendAsync(request, cancellationToken);
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "forgerems-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
