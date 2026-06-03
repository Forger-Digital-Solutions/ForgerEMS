using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class QuarantineDownloadServiceTests
{
    [Fact]
    public async Task DownloadAsync_QuarantinedOnlyWhenPayloadExistsUnderQuarantineRoot()
    {
        var root = NewTempRoot();
        try
        {
            var payload = Encoding.UTF8.GetBytes("FORGEREMS-FAKE-EICAR-LIKE-TEST-NOT-MALWARE");
            using var client = new HttpClient(new DelegateHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(payload)
                }));

            var result = await QuarantineDownloadService.DownloadAsync(
                "https://example.test/file.bin",
                client,
                new QuarantineDownloadOptions { QuarantineRoot = root });

            Assert.Equal(QuarantineOutcome.Quarantined, result.Outcome);
            Assert.True(result.FinalFileExists);
            Assert.True(File.Exists(result.PayloadPath));
            Assert.StartsWith(Path.GetFullPath(root), Path.GetFullPath(result.PayloadPath!), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("payload.forgerq", Path.GetFileName(result.PayloadPath));
        }
        finally
        {
            DeleteTree(root);
        }
    }

    [Fact]
    public async Task DownloadAsync_WritesMetadataSidecarForSuccessfulQuarantine()
    {
        var root = NewTempRoot();
        try
        {
            using var client = new HttpClient(new DelegateHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("harmless quarantine payload"))
                }));

            var result = await QuarantineDownloadService.DownloadAsync(
                "https://example.test/payload.bin",
                client,
                new QuarantineDownloadOptions { QuarantineRoot = root });

            Assert.Equal(QuarantineOutcome.Quarantined, result.Outcome);
            Assert.True(File.Exists(result.MetadataPath));
            var json = File.ReadAllText(result.MetadataPath!);
            Assert.Contains("\"outcome\": \"Quarantined\"", json, StringComparison.Ordinal);
            Assert.Contains("\"sha256\"", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTree(root);
        }
    }

    [Fact]
    public async Task DownloadAsync_IncludesSha256AndByteCountForHarmlessPayload()
    {
        var root = NewTempRoot();
        try
        {
            var payload = Encoding.UTF8.GetBytes("harmless-test-server-payload");
            using var client = new HttpClient(new DelegateHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(payload)
                }));

            var result = await QuarantineDownloadService.DownloadAsync(
                "https://example.test/payload.bin",
                client,
                new QuarantineDownloadOptions { QuarantineRoot = root });

            Assert.Equal(payload.Length, result.BytesWritten);
            Assert.Equal(payload.Length, result.BytesAttempted);
            Assert.NotNull(result.Sha256Hex);
            Assert.Equal(64, result.Sha256Hex!.Length);
            Assert.Contains(SafetyCheckSeverity.HashComputed, result.States);
        }
        finally
        {
            DeleteTree(root);
        }
    }

    [Fact]
    public async Task DownloadAsync_FileDisappearsAfterWriteReportsExternalSecurityNotQuarantined()
    {
        var root = NewTempRoot();
        try
        {
            using var client = new HttpClient(new DelegateHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("harmless payload that disappears"))
                }));

            var result = await QuarantineDownloadService.DownloadAsync(
                "https://secure.eicar.org/eicar.com.txt",
                client,
                new QuarantineDownloadOptions
                {
                    QuarantineRoot = root,
                    AfterPayloadWriteAsync = (path, _) =>
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                        File.Delete(path);
                        return Task.CompletedTask;
                    }
                });

            Assert.Equal(QuarantineOutcome.BlockedByExternalSecurity, result.Outcome);
            Assert.False(result.FinalFileExists);
            Assert.True(result.ExternalSecurityLikelyIntercepted);
            Assert.Contains(SafetyCheckSeverity.BlockedByExternalSecurity, result.States);
        }
        finally
        {
            DeleteTree(root);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task DownloadAsync_HttpErrorsBecomeDownloadFailed(HttpStatusCode statusCode)
    {
        var root = NewTempRoot();
        try
        {
            using var client = new HttpClient(new DelegateHandler(_ =>
                new HttpResponseMessage(statusCode)
                {
                    Content = new ByteArrayContent(Array.Empty<byte>())
                }));

            var result = await QuarantineDownloadService.DownloadAsync(
                "https://example.test/missing.bin",
                client,
                new QuarantineDownloadOptions { QuarantineRoot = root });

            Assert.Equal(QuarantineOutcome.DownloadFailed, result.Outcome);
            Assert.Contains(SafetyCheckSeverity.DownloadFailed, result.States);
            Assert.True(File.Exists(result.MetadataPath));
        }
        finally
        {
            DeleteTree(root);
        }
    }

    [Fact]
    public async Task DownloadAsync_MaxDownloadSizeIsEnforced()
    {
        var root = NewTempRoot();
        try
        {
            using var client = new HttpClient(new DelegateHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("0123456789"))
                }));

            var result = await QuarantineDownloadService.DownloadAsync(
                "https://example.test/large.bin",
                client,
                new QuarantineDownloadOptions { QuarantineRoot = root, MaxDownloadBytes = 4 });

            Assert.Equal(QuarantineOutcome.RejectedByPolicy, result.Outcome);
            Assert.Contains(SafetyCheckSeverity.RejectedByPolicy, result.States);
            Assert.NotEqual(QuarantineOutcome.Quarantined, result.Outcome);
        }
        finally
        {
            DeleteTree(root);
        }
    }

    [Fact]
    public async Task CreateInternalSelfTestAsync_WritesHarmlessOfflineQuarantineFixture()
    {
        var root = NewTempRoot();
        try
        {
            var result = await QuarantineDownloadService.CreateInternalSelfTestAsync(root);

            Assert.Equal(QuarantineOutcome.Quarantined, result.Outcome);
            Assert.True(File.Exists(result.PayloadPath));
            Assert.Equal(KnownSecurityTestFixtureRecognizer.InternalSelfTestPayload.Length, result.BytesWritten);
            Assert.Contains("internal harmless self-test", result.Verdict, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTree(root);
        }
    }

    private static string NewTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "forgerems-quarantine-tests-" + Guid.NewGuid().ToString("N"));
    }

    private static void DeleteTree(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            catch
            {
            }
        }

        Directory.Delete(root, recursive: true);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
