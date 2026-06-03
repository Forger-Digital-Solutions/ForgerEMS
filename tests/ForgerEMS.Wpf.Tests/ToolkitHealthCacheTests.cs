using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Integration tests for the toolkit health verification cache (Part A of the
/// v1.2.3-preview.1 follow-up pass). They drive the real
/// Get-ForgerEMSToolkitHealth.ps1 script with a tiny synthetic manifest and
/// inspect both the JSON report and the persisted cache JSON.
/// </summary>
public sealed class ToolkitHealthCacheTests
{
    [Fact]
    public void FirstScan_HashesFreshly_AndPersistsCache()
    {
        using var fixture = new ToolkitHealthCacheFixture();
        fixture.WriteVerifiedSystemRescue();

        var first = fixture.RunScan();
        var summary = first.RootElement.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("installed").GetInt32());

        var telemetry = first.RootElement.GetProperty("cacheTelemetry");
        Assert.Equal("FastCached", telemetry.GetProperty("mode").GetString());
        Assert.Equal(0, telemetry.GetProperty("cachedItemReuseCount").GetInt32());
        Assert.Equal(1, telemetry.GetProperty("freshlyHashedCount").GetInt32());
        Assert.True(telemetry.GetProperty("cacheSaved").GetBoolean());

        var cache = fixture.LoadCache();
        Assert.NotNull(cache);
        var item = cache!.RootElement
            .GetProperty("targets").EnumerateObject().Single().Value
            .GetProperty("items").EnumerateObject().Single().Value;
        Assert.Equal("INSTALLED", item.GetProperty("status").GetString());
        Assert.Equal("Match", item.GetProperty("checksumStatus").GetString());
        Assert.Equal(fixture.ExpectedSha256, item.GetProperty("actualChecksum").GetString());
    }

    [Fact]
    public void SecondScan_UnchangedFile_ReusesCacheWithoutRehash()
    {
        using var fixture = new ToolkitHealthCacheFixture();
        fixture.WriteVerifiedSystemRescue();

        fixture.RunScan();
        var second = fixture.RunScan();

        var telemetry = second.RootElement.GetProperty("cacheTelemetry");
        Assert.Equal(1, telemetry.GetProperty("cachedItemReuseCount").GetInt32());
        Assert.Equal(0, telemetry.GetProperty("freshlyHashedCount").GetInt32());

        var item = second.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("INSTALLED", item.GetProperty("status").GetString());
        Assert.Equal("Match", item.GetProperty("checksumStatus").GetString());
        // Cached items must not claim a fresh hash — wording check matches the
        // PowerShell side ("cached match (unchanged since previous verified scan...)").
        var verification = item.GetProperty("verification").GetString() ?? string.Empty;
        Assert.Contains("cached match", verification, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("verified.", verification, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("cached", item.GetProperty("verificationMode").GetString());
    }

    [Fact]
    public void ChangedFileSize_ForcesRehash()
    {
        using var fixture = new ToolkitHealthCacheFixture();
        fixture.WriteVerifiedSystemRescue();
        fixture.RunScan();

        fixture.MutateIsoToDifferentSize();
        var second = fixture.RunScan();

        var item = second.RootElement.GetProperty("items").EnumerateArray().Single();
        // Different bytes now → no longer matches the original expected hash, so
        // the rehash should produce a hash mismatch verdict.
        Assert.Equal("HASH_FAILED", item.GetProperty("status").GetString());

        var telemetry = second.RootElement.GetProperty("cacheTelemetry");
        Assert.Equal(0, telemetry.GetProperty("cachedItemReuseCount").GetInt32());
        Assert.Equal(1, telemetry.GetProperty("freshlyHashedCount").GetInt32());
    }

    [Fact]
    public void ChangedLastWriteTime_ForcesRehash()
    {
        using var fixture = new ToolkitHealthCacheFixture();
        fixture.WriteVerifiedSystemRescue();
        fixture.RunScan();

        fixture.BumpIsoLastWrite();
        var second = fixture.RunScan();

        var telemetry = second.RootElement.GetProperty("cacheTelemetry");
        Assert.Equal(0, telemetry.GetProperty("cachedItemReuseCount").GetInt32());
        Assert.Equal(1, telemetry.GetProperty("freshlyHashedCount").GetInt32());
    }

    [Fact]
    public void ChangedExpectedChecksum_ForcesRehash()
    {
        using var fixture = new ToolkitHealthCacheFixture();
        fixture.WriteVerifiedSystemRescue();
        fixture.RunScan();

        fixture.RewriteChecksumFileWithUnrelatedHash();
        var second = fixture.RunScan();

        var item = second.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("HASH_FAILED", item.GetProperty("status").GetString());
        var telemetry = second.RootElement.GetProperty("cacheTelemetry");
        Assert.Equal(0, telemetry.GetProperty("cachedItemReuseCount").GetInt32());
        Assert.Equal(1, telemetry.GetProperty("freshlyHashedCount").GetInt32());
    }

    [Fact]
    public void ChangedManifest_ForcesRehash()
    {
        using var fixture = new ToolkitHealthCacheFixture();
        fixture.WriteVerifiedSystemRescue();
        fixture.RunScan();

        // Adding any item changes the manifest content hash → the cache target
        // entry is invalidated even though the existing file is identical.
        fixture.AppendInertManifestItem();
        var second = fixture.RunScan();

        var telemetry = second.RootElement.GetProperty("cacheTelemetry");
        Assert.Equal(0, telemetry.GetProperty("cachedItemReuseCount").GetInt32());
        Assert.True(telemetry.GetProperty("freshlyHashedCount").GetInt32() >= 1);
    }

    [Fact]
    public void FullVerifyMode_IgnoresCacheAndRehashes()
    {
        using var fixture = new ToolkitHealthCacheFixture();
        fixture.WriteVerifiedSystemRescue();
        fixture.RunScan();

        var deep = fixture.RunScan(fullVerify: true);
        var telemetry = deep.RootElement.GetProperty("cacheTelemetry");
        Assert.Equal("FullVerify", telemetry.GetProperty("mode").GetString());
        Assert.Equal(0, telemetry.GetProperty("cachedItemReuseCount").GetInt32());
        Assert.Equal(1, telemetry.GetProperty("freshlyHashedCount").GetInt32());
        Assert.Equal("full-verify-mode", telemetry.GetProperty("cacheReason").GetString());

        var item = deep.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("INSTALLED", item.GetProperty("status").GetString());
        Assert.Equal("fresh", item.GetProperty("verificationMode").GetString());
    }

    [Fact]
    public void CorruptCache_FallsBackSafely_AndRehashes()
    {
        using var fixture = new ToolkitHealthCacheFixture();
        fixture.WriteVerifiedSystemRescue();
        fixture.RunScan();

        fixture.CorruptCacheFile();
        var second = fixture.RunScan();

        var telemetry = second.RootElement.GetProperty("cacheTelemetry");
        Assert.False(telemetry.GetProperty("cacheLoaded").GetBoolean());
        Assert.Equal("cache-parse-failed", telemetry.GetProperty("cacheReason").GetString());
        Assert.Equal(0, telemetry.GetProperty("cachedItemReuseCount").GetInt32());
        Assert.Equal(1, telemetry.GetProperty("freshlyHashedCount").GetInt32());
    }

    [Fact]
    public void StoredSerial_WithCurrentSerialUnavailable_DoesNotReuseCache()
    {
        // Safety hole audit fix: if the previous scan persisted a known volume
        // serial (e.g., Get-Volume worked), and the current scan cannot read a
        // serial (Get-Volume / CIM both fail), the cache MUST NOT be reused —
        // we never downgrade historical identity trust.
        using var fixture = new ToolkitHealthCacheFixture();
        fixture.WriteVerifiedSystemRescue();
        fixture.RunScan();

        // Forge the cache so the stored entry has a non-empty volumeSerial
        // but no current scan can read one (the scan target is a plain temp
        // directory with no real volume serial available to the script).
        fixture.MutateCacheVolumeSerial("\\\\?\\Volume{deadbeef-0000-0000-0000-000000000000}\\");

        var second = fixture.RunScan();
        var telemetry = second.RootElement.GetProperty("cacheTelemetry");
        Assert.Equal(0, telemetry.GetProperty("cachedItemReuseCount").GetInt32());
        Assert.Equal(1, telemetry.GetProperty("freshlyHashedCount").GetInt32());
    }

    [Fact]
    public void EmptyStoredSerial_StillReusesCache_BackwardsCompatible()
    {
        // Inverse of the previous test: if a historical cache was saved with
        // no serial (older snapshot, or volume APIs failed at save time), the
        // current scan should still be able to reuse it via path / size /
        // last-write / expected-checksum protection.
        using var fixture = new ToolkitHealthCacheFixture();
        fixture.WriteVerifiedSystemRescue();
        fixture.RunScan();

        fixture.MutateCacheVolumeSerial(string.Empty);

        var second = fixture.RunScan();
        var telemetry = second.RootElement.GetProperty("cacheTelemetry");
        Assert.Equal(1, telemetry.GetProperty("cachedItemReuseCount").GetInt32());
        Assert.Equal(0, telemetry.GetProperty("freshlyHashedCount").GetInt32());
    }

    [Fact]
    public void CachedMatchWording_DropsTimestamp_WhenVerifiedUtcMissing()
    {
        // Cached-match wording must not leak a dangling "at ." when an older
        // cache entry has no verifiedUtc field.
        using var fixture = new ToolkitHealthCacheFixture();
        fixture.WriteVerifiedSystemRescue();
        fixture.RunScan();

        fixture.StripCachedVerifiedUtc();

        var second = fixture.RunScan();
        var item = second.RootElement.GetProperty("items").EnumerateArray().Single();
        var verification = item.GetProperty("verification").GetString() ?? string.Empty;
        Assert.Contains("cached match", verification, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("at .", verification, StringComparison.Ordinal);
        Assert.DoesNotContain("at )", verification, StringComparison.Ordinal);
    }

    [Fact]
    public void AtomicCacheWrite_LeavesNoLingeringTempFile()
    {
        // Hardening: the cache writer stages to a .tmp and renames into place.
        // After a normal scan, the temp file must not be left behind.
        using var fixture = new ToolkitHealthCacheFixture();
        fixture.WriteVerifiedSystemRescue();
        fixture.RunScan();

        Assert.False(File.Exists(fixture.CachePath + ".tmp"),
            "ToolkitHealthCache temp file leaked after scan.");
        Assert.True(File.Exists(fixture.CachePath), "Cache file should exist after scan.");
    }

    [Fact]
    public void PriorMismatch_IsNeverReusedAsCacheHit()
    {
        using var fixture = new ToolkitHealthCacheFixture();
        // Start with a mismatched file: write the file but use an unrelated
        // checksum source so the first scan records HASH_FAILED — the cache
        // must not turn around and bless that file as cached match next time.
        fixture.WriteIsoWithUnrelatedChecksum();
        var first = fixture.RunScan();
        var firstItem = first.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("HASH_FAILED", firstItem.GetProperty("status").GetString());

        // Now repair: write the matching checksum and rerun. The cache must
        // not say "cached match" — there was no prior INSTALLED+Match entry to
        // reuse. The new scan should hash freshly and report INSTALLED.
        fixture.WriteMatchingChecksumForExistingIso();
        var second = fixture.RunScan();
        var secondItem = second.RootElement.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("INSTALLED", secondItem.GetProperty("status").GetString());
        Assert.Equal("fresh", secondItem.GetProperty("verificationMode").GetString());
        var telemetry = second.RootElement.GetProperty("cacheTelemetry");
        Assert.Equal(0, telemetry.GetProperty("cachedItemReuseCount").GetInt32());
        Assert.Equal(1, telemetry.GetProperty("freshlyHashedCount").GetInt32());
    }

    private sealed class ToolkitHealthCacheFixture : IDisposable
    {
        private readonly string _root;
        private readonly string _isoPath;
        private readonly string _checksumPath;
        private readonly string _manifestPath;
        private readonly string _localReportsPath;
        private string _expectedSha256 = string.Empty;

        public ToolkitHealthCacheFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), "forgerems-toolkit-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);

            _isoPath = Path.Combine(_root, "ISO", "Linux", "systemrescue-13.00-amd64.iso");
            Directory.CreateDirectory(Path.GetDirectoryName(_isoPath)!);
            _checksumPath = Path.Combine(_root, "systemrescue.sha256");
            _manifestPath = Path.Combine(_root, "manifest.json");
            _localReportsPath = Path.Combine(_root, "_local-reports");
        }

        public string ExpectedSha256 => _expectedSha256;

        public string CachePath => Path.Combine(_localReportsPath, "toolkit-health-cache.json");

        public void WriteVerifiedSystemRescue()
        {
            File.WriteAllText(_isoPath, "verified-systemrescue");
            _expectedSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_isoPath))).ToLowerInvariant();
            File.WriteAllText(_checksumPath, $"{_expectedSha256}  systemrescue-13.00-amd64.iso");
            WriteManifest(extra: string.Empty);
        }

        public void WriteIsoWithUnrelatedChecksum()
        {
            File.WriteAllText(_isoPath, "different-bytes");
            // Compute a hash for content that is NOT the iso so the first scan
            // records HASH_FAILED.
            var bogus = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("unrelated")))
                .ToLowerInvariant();
            _expectedSha256 = bogus;
            File.WriteAllText(_checksumPath, $"{bogus}  systemrescue-13.00-amd64.iso");
            WriteManifest(extra: string.Empty);
        }

        public void WriteMatchingChecksumForExistingIso()
        {
            _expectedSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_isoPath))).ToLowerInvariant();
            File.WriteAllText(_checksumPath, $"{_expectedSha256}  systemrescue-13.00-amd64.iso");
        }

        public void MutateIsoToDifferentSize()
        {
            File.WriteAllText(_isoPath, "verified-systemrescue-now-much-larger-content");
        }

        public void BumpIsoLastWrite()
        {
            var info = new FileInfo(_isoPath);
            info.LastWriteTimeUtc = info.LastWriteTimeUtc.AddMinutes(7);
        }

        public void RewriteChecksumFileWithUnrelatedHash()
        {
            var bogus = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("rotated")))
                .ToLowerInvariant();
            File.WriteAllText(_checksumPath, $"{bogus}  systemrescue-13.00-amd64.iso");
        }

        public void AppendInertManifestItem()
        {
            // Add a second item to change the manifest hash without touching the
            // existing file. Using a missing-file shortcut keeps the test stable.
            WriteManifest(extra: ",{\"name\":\"Inert Marker\",\"type\":\"page\",\"dest\":\"ISO\\\\Linux\\\\DOWNLOAD - Inert.url\",\"url\":\"https://example.test/inert\",\"enabled\":true}");
        }

        public void CorruptCacheFile()
        {
            File.WriteAllText(CachePath, "{ this is not valid json ");
        }

        public void MutateCacheVolumeSerial(string newSerial)
        {
            // Forge the volumeSerial field on the only target entry in the
            // cache so we can exercise the strict identity rule without
            // having a real USB volume in the test environment.
            using var doc = JsonDocument.Parse(File.ReadAllText(CachePath));
            var targets = doc.RootElement.GetProperty("targets");
            var targetKey = targets.EnumerateObject().Single().Name;
            var entry = targets.GetProperty(targetKey);

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var rootProp in doc.RootElement.EnumerateObject())
                {
                    if (rootProp.Name != "targets")
                    {
                        rootProp.WriteTo(writer);
                        continue;
                    }

                    writer.WritePropertyName("targets");
                    writer.WriteStartObject();
                    writer.WritePropertyName(targetKey);
                    writer.WriteStartObject();
                    foreach (var prop in entry.EnumerateObject())
                    {
                        if (prop.Name == "volumeSerial")
                        {
                            writer.WriteString("volumeSerial", newSerial);
                            continue;
                        }

                        prop.WriteTo(writer);
                    }

                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            File.WriteAllBytes(CachePath, stream.ToArray());
        }

        public void StripCachedVerifiedUtc()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(CachePath));
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                WriteWithItemMutation(doc.RootElement, writer, (itemWriter, item) =>
                {
                    itemWriter.WriteStartObject();
                    foreach (var prop in item.EnumerateObject())
                    {
                        if (prop.Name == "verifiedUtc")
                        {
                            continue;
                        }

                        prop.WriteTo(itemWriter);
                    }

                    itemWriter.WriteEndObject();
                });
            }

            File.WriteAllBytes(CachePath, stream.ToArray());
        }

        private static void WriteWithItemMutation(
            JsonElement root,
            Utf8JsonWriter writer,
            Action<Utf8JsonWriter, JsonElement> writeItem)
        {
            writer.WriteStartObject();
            foreach (var rootProp in root.EnumerateObject())
            {
                if (rootProp.Name != "targets")
                {
                    rootProp.WriteTo(writer);
                    continue;
                }

                writer.WritePropertyName("targets");
                writer.WriteStartObject();
                foreach (var target in rootProp.Value.EnumerateObject())
                {
                    writer.WritePropertyName(target.Name);
                    writer.WriteStartObject();
                    foreach (var targetProp in target.Value.EnumerateObject())
                    {
                        if (targetProp.Name != "items")
                        {
                            targetProp.WriteTo(writer);
                            continue;
                        }

                        writer.WritePropertyName("items");
                        writer.WriteStartObject();
                        foreach (var item in targetProp.Value.EnumerateObject())
                        {
                            writer.WritePropertyName(item.Name);
                            writeItem(writer, item.Value);
                        }

                        writer.WriteEndObject();
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        public JsonDocument RunScan(bool fullVerify = false)
        {
            var script = FindRepoFile("backend", "ToolkitManager", "Get-ForgerEMSToolkitHealth.ps1");
            var exe = OperatingSystem.IsWindows() ? "powershell" : "pwsh";
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(script);
            psi.ArgumentList.Add("-TargetRoot");
            psi.ArgumentList.Add(_root);
            psi.ArgumentList.Add("-ManifestPath");
            psi.ArgumentList.Add(_manifestPath);
            if (fullVerify)
            {
                psi.ArgumentList.Add("-FullVerify");
            }

            psi.Environment["FORGEREMS_TOOLKIT_HEALTH_REPORT_ROOT"] = _localReportsPath;

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("PowerShell did not start.");
            Assert.True(process.WaitForExit(60_000), "Toolkit health script timed out.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            Assert.True(process.ExitCode == 0, stdout + Environment.NewLine + stderr);
            var reportText = File.ReadAllText(Path.Combine(_localReportsPath, "toolkit-health-latest.json"));
            return JsonDocument.Parse(reportText);
        }

        public JsonDocument? LoadCache()
        {
            return File.Exists(CachePath)
                ? JsonDocument.Parse(File.ReadAllText(CachePath))
                : null;
        }

        private void WriteManifest(string extra)
        {
            var checksumJsonPath = _checksumPath.Replace(@"\", @"\\");
            var manifest = "{\"items\":[" +
                "{\"name\":\"SystemRescue 13.00 (amd64)\",\"type\":\"file\",\"dest\":\"ISO\\\\Linux\\\\systemrescue-13.00-amd64.iso\",\"url\":\"https://example.test/systemrescue.iso\",\"sha256Url\":\"" + checksumJsonPath + "\",\"enabled\":true}" +
                extra +
                "]}";
            File.WriteAllText(_manifestPath, manifest);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup; tests are temp-dir based.
            }
        }
    }

    private static string FindRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate repo file.", Path.Combine(segments));
    }
}
