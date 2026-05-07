using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class VentoyIntegrationServiceResolutionTests
{
    [Fact]
    public async Task GetStatusAsync_UsesLatestOfficialReleaseWhenAvailable()
    {
        using var root = new TempDir();
        WritePinnedManifest(root.Path, "1.1.11", "aaaabbbbcccc1111222233334444555566667777888899990000aaaabbbbcccc");
        var latestSha = new string('a', 64);
        var service = BuildService(root.Path, (request, _) =>
        {
            if (request.RequestUri!.AbsoluteUri.Contains("/releases/latest", StringComparison.OrdinalIgnoreCase))
            {
                var json = """
                    {
                      "tag_name":"v1.1.12",
                      "body":"",
                      "assets":[
                        {"name":"ventoy-1.1.12-windows.zip","browser_download_url":"https://example.test/ventoy-1.1.12-windows.zip"},
                        {"name":"sha256.txt","browser_download_url":"https://example.test/sha256.txt"}
                      ]
                    }
                    """;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{latestSha}  ventoy-1.1.12-windows.zip")
            });
        });

        var status = await service.GetStatusAsync(BuildContext(root.Path), null);
        Assert.Contains("1.1.12", status.PackageText, StringComparison.Ordinal);
        Assert.Contains("Latest official release", status.PackageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStatusAsync_FallsBackToPinnedWhenLatestLookupFails()
    {
        using var root = new TempDir();
        WritePinnedManifest(root.Path, "1.1.11", new string('b', 64));
        var service = BuildService(root.Path, (_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)));

        var status = await service.GetStatusAsync(BuildContext(root.Path), null);
        Assert.Contains("Pinned fallback", status.PackageText, StringComparison.Ordinal);
        Assert.Contains("1.1.11", status.PackageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStatusAsync_UsesCachedLatestLabelWhenPinnedMatchesResolvedLatest()
    {
        using var root = new TempDir();
        var sha = new string('c', 64);
        WritePinnedManifest(root.Path, "1.1.12", sha);
        var service = BuildService(root.Path, (request, _) =>
        {
            if (request.RequestUri!.AbsoluteUri.Contains("/releases/latest", StringComparison.OrdinalIgnoreCase))
            {
                var json = """
                    {
                      "tag_name":"v1.1.12",
                      "body":"",
                      "assets":[
                        {"name":"ventoy-1.1.12-windows.zip","browser_download_url":"https://example.test/ventoy-1.1.12-windows.zip"},
                        {"name":"sha256.txt","browser_download_url":"https://example.test/sha256.txt"}
                      ]
                    }
                    """;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{sha}  ventoy-1.1.12-windows.zip")
            });
        });

        var status = await service.GetStatusAsync(BuildContext(root.Path), null);
        Assert.Contains("Cached latest", status.PackageText, StringComparison.Ordinal);
    }

    private static BackendContext BuildContext(string root) => new()
    {
        IsAvailable = true,
        Mode = BackendMode.Repo,
        RootPath = root,
        WorkingDirectory = root
    };

    private static void WritePinnedManifest(string root, string version, string sha)
    {
        var manifests = Path.Combine(root, "manifests");
        Directory.CreateDirectory(manifests);
        var json = $$"""
            {
              "items": [
                {
                  "name": "Ventoy {{version}} (Windows package)",
                  "type": "file",
                  "dest": "Tools\\Portable\\USB\\ventoy-{{version}}-windows.zip",
                  "url": "https://sourceforge.net/projects/ventoy/files/v{{version}}/ventoy-{{version}}-windows.zip/download",
                  "sha256": "{{sha}}"
                }
              ]
            }
            """;
        File.WriteAllText(Path.Combine(manifests, "ForgerEMS.updates.json"), json);
    }

    private static VentoyIntegrationService BuildService(string root, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
    {
        var client = new HttpClient(new DelegateHandler(sendAsync));
        return new VentoyIntegrationService(new NoopPowerShellRunner(), new AppRuntimeService(), client);
    }

    private sealed class NoopPowerShellRunner : IPowerShellRunnerService
    {
        public Task<PowerShellRunResult> RunAsync(PowerShellRunRequest request, Action<LogLine>? onOutput = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PowerShellRunResult());
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            sendAsync(request, cancellationToken);
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "forgerems-tests-" + Guid.NewGuid().ToString("N"));
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
