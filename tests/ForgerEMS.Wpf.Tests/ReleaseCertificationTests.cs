using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

/// <summary>
/// Regression tests for the release-certification gate: the pure policy core in
/// tools/ForgerEMS.ReleaseCertification.psm1 (invoked via PowerShell subprocess
/// fixtures, matching the MechanicalRcReleaseVerificationTests pattern), the
/// production endpoint wrapper, and the static workflow guard that prevents a
/// legacy release.json productionEligible=true from ever promoting a build.
/// </summary>
public sealed class ReleaseCertificationTests
{
    private static readonly string RepoRoot = LocateRepoRoot();
    private static readonly string ModulePath = Path.Combine(RepoRoot, "tools", "ForgerEMS.ReleaseCertification.psm1");
    private static readonly string EndpointPath = Path.Combine(RepoRoot, "tools", "Test-ForgerEMSReleaseCertification.ps1");
    private static readonly string SignerPath = Path.Combine(RepoRoot, "tools", "Protect-ForgerEMSReleaseReceipt.ps1");

    private static readonly string[] MandatoryGates =
    {
        "Build", "Tests", "Integrity", "Signing", "Portable", "Security", "Policy",
        "CleanInstall", "Upgrade", "Uninstall", "ResidueAudit", "Reinstall",
        "DriverServiceTaskAudit", "GuiValidation"
    };
    private static readonly HashSet<string> LifecycleGates = new(StringComparer.Ordinal)
    {
        "CleanInstall", "Upgrade", "Uninstall", "ResidueAudit", "Reinstall",
        "DriverServiceTaskAudit", "GuiValidation"
    };

    private const string SourceHead = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string InstallerSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    // ---------- Fixture builders -------------------------------------------------

    private sealed class Fixture : IDisposable
    {
        public string Root { get; }
        public string CandidatePath { get; }
        public string ReceiptDir { get; }
        public string FactsPath { get; }
        public string ManifestSha { get; private set; } = "";
        public readonly string BuildId = Guid.NewGuid().ToString("D");
        public readonly string GeneratedUtc = DateTimeOffset.UtcNow.AddHours(-2).ToString("o");

        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "forgerems-cert-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            ReceiptDir = Path.Combine(Root, "receipts");
            Directory.CreateDirectory(ReceiptDir);
            CandidatePath = Path.Combine(Root, "candidate-certification.json");
            FactsPath = Path.Combine(Root, "facts.json");
        }

        public void WriteCandidate(string architecture = "x64", bool includeUninstaller = true, int? schemaVersion = 1,
            string? buildId = null)
        {
            var artifacts = new List<object>
            {
                new { role = "Installer", filename = "ForgerEMS-Setup-v1.2.4.exe", sha256 = InstallerSha, sizeBytes = 100 },
                new { role = "Portable", filename = "ForgerEMS-v1.2.4.zip", sha256 = new string('c', 64), sizeBytes = 200 },
                new { role = "Frontend", filename = "app/ForgerEMS.exe", sha256 = new string('d', 64), sizeBytes = 300 }
            };
            if (includeUninstaller)
            {
                artifacts.Add(new { role = "Uninstaller", filename = "uninstaller/verified-uninstaller.exe", sha256 = new string('e', 64), sizeBytes = 50 });
            }
            var doc = new Dictionary<string, object?>
            {
                ["schemaVersion"] = schemaVersion ?? 1,
                ["buildId"] = buildId ?? BuildId,
                ["generatedUtc"] = GeneratedUtc,
                ["sourceHead"] = SourceHead,
                ["version"] = "1.2.4",
                ["architecture"] = architecture,
                ["runtime"] = "win-x64",
                ["sourceDirtyFileCount"] = 0,
                ["artifacts"] = artifacts
            };
            File.WriteAllText(CandidatePath, JsonSerializer.Serialize(doc));
            ManifestSha = Sha256OfFile(CandidatePath);
        }

        public void WriteReceipt(string gate, string result = "PASS", string fileName = "",
            Dictionary<string, object?>? overrides = null, int? schemaVersion = 1)
        {
            var leaf = Path.Combine(ReceiptDir, $"{gate}-evidence.txt");
            if (!File.Exists(leaf)) File.WriteAllText(leaf, $"evidence for {gate}");
            var started = DateTimeOffset.Parse(GeneratedUtc).AddMinutes(10);
            var doc = new Dictionary<string, object?>
            {
                ["schemaVersion"] = schemaVersion,
                ["gate"] = gate,
                ["result"] = result,
                ["sourceHead"] = SourceHead,
                ["version"] = "1.2.4",
                ["architecture"] = "x64",
                ["buildId"] = BuildId,
                ["manifestSha256"] = ManifestSha,
                ["installerFilename"] = "ForgerEMS-Setup-v1.2.4.exe",
                ["installerSha256"] = InstallerSha,
                ["generatedUtc"] = started.AddMinutes(15).ToString("o"),
                ["startedUtc"] = started.ToString("o"),
                ["completedUtc"] = started.AddMinutes(10).ToString("o"),
                ["evidence"] = new[] { new { path = $"{gate}-evidence.txt", sha256 = Sha256OfFile(leaf) } }
            };
            if (LifecycleGates.Contains(gate))
            {
                doc["guest"] = new
                {
                    vmId = "e86b6629-1d60-44b3-88c1-32d6a74bdb21",
                    osVersion = "10.0.26100",
                    osBuild = "26100",
                    architecture = "x64",
                    isolationKind = "QEMU"
                };
            }
            if (overrides != null)
            {
                foreach (var kv in overrides) doc[kv.Key] = kv.Value;
            }
            File.WriteAllText(Path.Combine(ReceiptDir, (fileName.Length > 0 ? fileName : gate) + ".json"),
                JsonSerializer.Serialize(doc));
        }

        public void WriteAllGates()
        {
            foreach (var gate in MandatoryGates) WriteReceipt(gate);
        }

        public void WriteFacts(Dictionary<string, object>? overrides = null)
        {
            var facts = new Dictionary<string, object>
            {
                ["ExpectedVersion"] = "1.2.4",
                ["CandidateManifestSha256"] = ManifestSha,
                ["SourceCommitExists"] = true,
                ["SourceTreeClean"] = true,
                ["SourceDeltaAllowed"] = true,
                ["ArtifactFilesVerified"] = true,
                ["ArtifactSignaturesVerified"] = true,
                ["PortableFrontendVerified"] = true,
                ["ReceiptSignaturesTrusted"] = true,
                ["ReceiptEvidenceVerified"] = true
            };
            if (overrides != null)
            {
                foreach (var kv in overrides) facts[kv.Key] = kv.Value;
            }
            File.WriteAllText(FactsPath, JsonSerializer.Serialize(facts));
        }

        public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
    }

    private static string Sha256OfFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static JsonDocument EvaluateCore(Fixture fixture)
    {
        var command =
            $"Import-Module '{ModulePath}' -Force; " +
            $"$c = Get-Content '{fixture.CandidatePath}' -Raw | ConvertFrom-Json; " +
            $"$r = @(Get-ChildItem '{fixture.ReceiptDir}' -Filter *.json | ForEach-Object {{ Get-Content $_.FullName -Raw | ConvertFrom-Json }}); " +
            $"$fp = Get-Content '{fixture.FactsPath}' -Raw | ConvertFrom-Json; " +
            "$f = @{}; $fp.PSObject.Properties | ForEach-Object { $f[$_.Name] = $_.Value }; " +
            "Get-ForgerEMSCertificationState -Candidate $c -Receipts $r -VerifiedFacts $f | ConvertTo-Json -Depth 8";
        return JsonDocument.Parse(RunPowerShell(command));
    }

    private static bool IsEligible(JsonDocument state) =>
        state.RootElement.GetProperty("productionEligible").GetBoolean();

    // ---------- Core policy cases -------------------------------------------------

    [Fact]
    public void Core_BuildOnlyFixture_DoesNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        f.WriteReceipt("Build"); f.WriteReceipt("Tests"); f.WriteReceipt("Integrity");
        f.WriteFacts();
        Assert.False(IsEligible(EvaluateCore(f)));
    }

    [Fact]
    public void Core_SignedOnlyFixture_DoesNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        foreach (var g in new[] { "Build", "Tests", "Integrity", "Signing", "Portable", "Security", "Policy" })
            f.WriteReceipt(g);
        f.WriteFacts();
        var state = EvaluateCore(f);
        Assert.False(IsEligible(state));
        Assert.False(state.RootElement.GetProperty("lifecycleEligible").GetBoolean());
    }

    [Fact]
    public void Core_LifecycleOnlyWithoutSigning_DoesNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        foreach (var g in LifecycleGates) f.WriteReceipt(g);
        f.WriteFacts(new Dictionary<string, object> { ["ArtifactSignaturesVerified"] = false });
        var state = EvaluateCore(f);
        Assert.False(IsEligible(state));
        Assert.False(state.RootElement.GetProperty("signingEligible").GetBoolean());
    }

    [Fact]
    public void Core_WrongInstallerSha_DoesNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        f.WriteAllGates();
        // Rewrite one receipt with a wrong installer hash binding.
        File.Delete(Path.Combine(f.ReceiptDir, "Build.json"));
        f.WriteReceipt("Build", overrides: new Dictionary<string, object?> { ["installerSha256"] = new string('f', 64) });
        f.WriteFacts();
        Assert.False(IsEligible(EvaluateCore(f)));
    }

    [Fact]
    public void Core_WrongSourceHead_DoesNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        f.WriteAllGates();
        File.Delete(Path.Combine(f.ReceiptDir, "Tests.json"));
        f.WriteReceipt("Tests", overrides: new Dictionary<string, object?> { ["sourceHead"] = new string('0', 40) });
        f.WriteFacts();
        Assert.False(IsEligible(EvaluateCore(f)));
    }

    [Fact]
    public void Core_FailedUninstall_DoesNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        f.WriteAllGates();
        File.Delete(Path.Combine(f.ReceiptDir, "Uninstall.json"));
        f.WriteReceipt("Uninstall", result: "FAIL");
        f.WriteFacts();
        Assert.False(IsEligible(EvaluateCore(f)));
    }

    [Fact]
    public void Core_CompleteTrustedFixture_Promotes()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        f.WriteAllGates();
        f.WriteFacts();
        var state = EvaluateCore(f);
        Assert.True(IsEligible(state),
            "A complete synthetic fixture with all facts trusted must be eligible; reasons: " +
            string.Join("; ", state.RootElement.GetProperty("blockingReasons").EnumerateArray().Select(e => e.GetString())));
    }

    [Fact]
    public void Core_PreviousVersionOrBuildId_DoesNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        f.WriteAllGates();
        File.Delete(Path.Combine(f.ReceiptDir, "Upgrade.json"));
        f.WriteReceipt("Upgrade", overrides: new Dictionary<string, object?>
        {
            ["version"] = "1.2.3",
            ["buildId"] = Guid.NewGuid().ToString("D")
        });
        f.WriteFacts();
        Assert.False(IsEligible(EvaluateCore(f)));
    }

    [Fact]
    public void Core_MissingReceiptSchema_DoesNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        f.WriteAllGates();
        File.Delete(Path.Combine(f.ReceiptDir, "Security.json"));
        f.WriteReceipt("Security", schemaVersion: null);
        f.WriteFacts();
        Assert.False(IsEligible(EvaluateCore(f)));
    }

    [Fact]
    public void Core_MalformedCandidateSchema_DoesNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate(schemaVersion: (int?)99);
        f.WriteAllGates();
        f.WriteFacts();
        Assert.False(IsEligible(EvaluateCore(f)));
    }

    [Fact]
    public void Core_DuplicateGate_DoesNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        f.WriteAllGates();
        f.WriteReceipt("Build", fileName: "Build-duplicate");
        f.WriteFacts();
        Assert.False(IsEligible(EvaluateCore(f)));
    }

    [Fact]
    public void Core_UntrustedReceipts_DoNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        f.WriteAllGates();
        f.WriteFacts(new Dictionary<string, object> { ["ReceiptSignaturesTrusted"] = false });
        Assert.False(IsEligible(EvaluateCore(f)));
    }

    [Fact]
    public void Core_WrongArchitecture_DoesNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate(architecture: "x86");
        f.WriteAllGates();
        f.WriteFacts();
        Assert.False(IsEligible(EvaluateCore(f)));
    }

    private static string Utc(DateTimeOffset t) => t.ToString("o");

    [Fact]
    public void Core_TimestampWithoutZone_DoesNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        f.WriteAllGates();
        File.Delete(Path.Combine(f.ReceiptDir, "Portable.json"));
        f.WriteReceipt("Portable", overrides: new Dictionary<string, object?>
        {
            ["startedUtc"] = "2026-10-08T12:00:00",      // no zone designator
            ["completedUtc"] = "2026-10-08T12:05:00",
            ["generatedUtc"] = "2026-10-08T12:06:00"
        });
        f.WriteFacts();
        Assert.False(IsEligible(EvaluateCore(f)));
    }

    [Fact]
    public void Core_NonUtcOffsetTimestamp_DoesNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        f.WriteAllGates();
        File.Delete(Path.Combine(f.ReceiptDir, "GuiValidation.json"));
        var s = DateTimeOffset.Parse(f.GeneratedUtc).AddMinutes(10);
        f.WriteReceipt("GuiValidation", overrides: new Dictionary<string, object?>
        {
            ["startedUtc"] = s.ToOffset(TimeSpan.FromHours(2)).ToString("o"),   // +02:00 offset
            ["completedUtc"] = s.AddMinutes(10).ToOffset(TimeSpan.FromHours(2)).ToString("o"),
            ["generatedUtc"] = s.AddMinutes(15).ToOffset(TimeSpan.FromHours(2)).ToString("o")
        });
        f.WriteFacts();
        Assert.False(IsEligible(EvaluateCore(f)));
    }

    [Fact]
    public void Core_LifecycleRunPredatingCandidate_DoesNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        f.WriteAllGates();
        File.Delete(Path.Combine(f.ReceiptDir, "CleanInstall.json"));
        // Lifecycle gates must observe the final signed hashes — a run that
        // predates the manifest can never be promotion evidence.
        var early = DateTimeOffset.Parse(f.GeneratedUtc).AddHours(-3);
        f.WriteReceipt("CleanInstall", overrides: new Dictionary<string, object?>
        {
            ["startedUtc"] = Utc(early),
            ["completedUtc"] = Utc(early.AddMinutes(10)),
            ["generatedUtc"] = Utc(DateTimeOffset.Parse(f.GeneratedUtc).AddMinutes(30))
        });
        f.WriteFacts();
        Assert.False(IsEligible(EvaluateCore(f)));
    }

    [Fact]
    public void Core_PreManifestGateMayPredateCandidate()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        f.WriteAllGates();
        // Build/Tests/Policy legitimately run before packaging — the signed
        // receipt (generatedUtc) is still bound to the manifest afterwards.
        File.Delete(Path.Combine(f.ReceiptDir, "Build.json"));
        var early = DateTimeOffset.Parse(f.GeneratedUtc).AddHours(-4);
        f.WriteReceipt("Build", overrides: new Dictionary<string, object?>
        {
            ["startedUtc"] = Utc(early),
            ["completedUtc"] = Utc(early.AddMinutes(10)),
            ["generatedUtc"] = Utc(DateTimeOffset.Parse(f.GeneratedUtc).AddMinutes(30))
        });
        f.WriteFacts();
        Assert.True(IsEligible(EvaluateCore(f)),
            "A Build gate run predating the manifest is the designed exemption and must not block.");
    }

    [Fact]
    public void Core_FutureTimestamp_DoesNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        f.WriteAllGates();
        File.Delete(Path.Combine(f.ReceiptDir, "ResidueAudit.json"));
        var future = DateTimeOffset.UtcNow.AddHours(2);
        f.WriteReceipt("ResidueAudit", overrides: new Dictionary<string, object?>
        {
            ["completedUtc"] = Utc(future), ["generatedUtc"] = Utc(future)
        });
        f.WriteFacts();
        Assert.False(IsEligible(EvaluateCore(f)));
    }

    [Fact]
    public void Core_ReversedTimestamps_DoNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        f.WriteAllGates();
        File.Delete(Path.Combine(f.ReceiptDir, "Security.json"));
        var s = DateTimeOffset.Parse(f.GeneratedUtc).AddMinutes(10);
        f.WriteReceipt("Security", overrides: new Dictionary<string, object?>
        {
            ["generatedUtc"] = Utc(s),                 // generated BEFORE its own completion
            ["completedUtc"] = Utc(s.AddMinutes(10))
        });
        f.WriteFacts();
        Assert.False(IsEligible(EvaluateCore(f)));
    }

    [Fact]
    public void Core_EmptyGuidBuildId_DoesNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate(buildId: Guid.Empty.ToString("D"));
        f.WriteAllGates();
        // Patch each receipt's buildId to the same empty GUID so the only
        // defect under test is the non-nonempty GUID itself.
        foreach (var path in Directory.GetFiles(f.ReceiptDir, "*.json"))
        {
            var doc = JsonDocument.Parse(File.ReadAllText(path));
            var dict = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
            dict["buildId"] = Guid.Empty.ToString("D");
            File.WriteAllText(path, JsonSerializer.Serialize(dict));
        }
        f.WriteFacts();
        Assert.False(IsEligible(EvaluateCore(f)));
    }

    [Fact]
    public void Core_UnknownExtraReceipt_DoesNotPromote()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        f.WriteAllGates();
        f.WriteReceipt("MysteryGate");
        f.WriteFacts();
        Assert.False(IsEligible(EvaluateCore(f)),
            "An unknown/malformed extra receipt must poison promotion even when all mandatory gates pass.");
    }

    [Fact]
    public void Core_MissingUninstallerRole_IsEngineeringOnlyAndRejected()
    {
        using var f = new Fixture();
        f.WriteCandidate(includeUninstaller: false);
        f.WriteAllGates();
        f.WriteFacts();
        Assert.False(IsEligible(EvaluateCore(f)));
    }

    // ---------- Real endpoint (wrapper) cases --------------------------------------

    /// <summary>
    /// Real wrapper: a receipt whose declared evidence hash does not match the
    /// actual leaf bytes must fail closed, and the emitted record must say so.
    /// </summary>
    [Fact]
    public void Endpoint_AlteredEvidenceHash_FailsClosed()
    {
        using var f = new Fixture();
        var releaseRoot = Path.Combine(f.Root, "release");
        Directory.CreateDirectory(releaseRoot);
        f.WriteCandidate();
        f.WriteAllGates();
        // Tamper: change the leaf after its hash was recorded in the receipt.
        File.WriteAllText(Path.Combine(f.ReceiptDir, "Build-evidence.txt"), "tampered evidence bytes");
        var output = Path.Combine(f.Root, "out", "release-certification.json");
        var result = RunPowerShellRaw(
            $"& '{EndpointPath}' -RepoRoot '{RepoRoot}' -ReleaseRoot '{releaseRoot}' " +
            $"-CandidateManifest '{f.CandidatePath}' -ReceiptDirectory '{f.ReceiptDir}' " +
            "-ApprovedReceiptSignerThumbprint '0000000000000000000000000000000000000000' " +
            "-ApprovedArtifactSignerThumbprint '0000000000000000000000000000000000000000' " +
            $"-OutputPath '{output}'", expectSuccess: false);
        Assert.NotEqual(0, result.ExitCode);
        var doc = JsonDocument.Parse(File.ReadAllText(output));
        Assert.False(doc.RootElement.GetProperty("productionEligible").GetBoolean());
        // The failure must be specifically the evidence-hash verification, not
        // any other blocking reason — assert the fact and its diagnostic.
        Assert.False(doc.RootElement.GetProperty("verifiedFacts").GetProperty("ReceiptEvidenceVerified").GetBoolean(),
            "ReceiptEvidenceVerified must be false on a tampered evidence leaf.");
        var diagnostics = doc.RootElement.GetProperty("receiptDiagnostics").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        Assert.True(diagnostics.Any(d => d != null && d.Contains("hash mismatch", StringComparison.OrdinalIgnoreCase)),
            "Expected an evidence hash-mismatch diagnostic; got: [" + string.Join("; ", diagnostics) + "]");
    }

    /// <summary>
    /// Real wrapper: receipts without any .p7s detached CMS signature can never
    /// satisfy the receipt-trust fact, so production eligibility stays false.
    /// </summary>
    [Fact]
    public void Endpoint_UnsignedReceipts_FailClosed()
    {
        using var f = new Fixture();
        var releaseRoot = Path.Combine(f.Root, "release");
        Directory.CreateDirectory(releaseRoot);
        f.WriteCandidate();
        f.WriteAllGates();
        var output = Path.Combine(f.Root, "out", "release-certification.json");
        var result = RunPowerShellRaw(
            $"& '{EndpointPath}' -RepoRoot '{RepoRoot}' -ReleaseRoot '{releaseRoot}' " +
            $"-CandidateManifest '{f.CandidatePath}' -ReceiptDirectory '{f.ReceiptDir}' " +
            "-ApprovedReceiptSignerThumbprint '0000000000000000000000000000000000000000' " +
            "-ApprovedArtifactSignerThumbprint '0000000000000000000000000000000000000000' " +
            $"-OutputPath '{output}'", expectSuccess: false);
        Assert.NotEqual(0, result.ExitCode);
        var doc = JsonDocument.Parse(File.ReadAllText(output));
        Assert.False(doc.RootElement.GetProperty("productionEligible").GetBoolean());
        // The failure must be specifically the receipt-trust fact: every
        // unsigned receipt must surface a 'missing detached .p7s' diagnostic.
        Assert.False(doc.RootElement.GetProperty("verifiedFacts").GetProperty("ReceiptSignaturesTrusted").GetBoolean(),
            "ReceiptSignaturesTrusted must be false for unsigned receipts.");
        var diagnostics = doc.RootElement.GetProperty("receiptDiagnostics").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        Assert.True(diagnostics.Any(d => d != null && d.Contains("missing detached .p7s", StringComparison.OrdinalIgnoreCase)),
            "Expected a missing-.p7s diagnostic; got: [" + string.Join("; ", diagnostics) + "]");
    }

    /// <summary>
    /// Real wrapper end-to-end: a receipt carrying a detached CMS signature made
    /// by an IN-MEMORY self-signed certificate (CertificateRequest/CreateSelfSigned
    /// — no cert-store mutation, no production helper) still cannot promote.
    /// The endpoint must decode the real .p7s, run CheckSignature(false), and
    /// fail closed on the untrusted chain — with diagnostics proving WHY.
    /// </summary>
    [Fact]
    public void Endpoint_SelfSignedReceipt_CannotPromote()
    {
        using var f = new Fixture();
        var releaseRoot = Path.Combine(f.Root, "release");
        Directory.CreateDirectory(releaseRoot);
        f.WriteCandidate();
        f.WriteAllGates();

        var receiptPath = Path.Combine(f.ReceiptDir, "Build.json");
        var p7sPath = receiptPath + ".p7s";
        // In-memory self-signed code-signing cert + detached CMS, written to the
        // fixture — exercises the endpoint's real decode/verify branch.
        RunPowerShell(
            "try { Add-Type -AssemblyName System.Security.Cryptography.Pkcs -ErrorAction Stop } " +
            "catch { Add-Type -AssemblyName System.Security }; " +
            "$rsa = [System.Security.Cryptography.RSA]::Create(2048); " +
            "$req = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(" +
            "'CN=Forger Digital Solutions', $rsa, " +
            "[System.Security.Cryptography.HashAlgorithmName]::SHA256, " +
            "[System.Security.Cryptography.RSASignaturePadding]::Pkcs1); " +
            "$oids = New-Object System.Security.Cryptography.OidCollection; " +
            "[void]$oids.Add((New-Object System.Security.Cryptography.Oid '1.3.6.1.5.5.7.3.3')); " +
            "$req.CertificateExtensions.Add((New-Object System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension($oids, $false))); " +
            "$cert = $req.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddDays(1)); " +
            $"$bytes = [IO.File]::ReadAllBytes('{receiptPath}'); " +
            "$content = New-Object System.Security.Cryptography.Pkcs.ContentInfo -ArgumentList (, $bytes); " +
            "$cms = New-Object System.Security.Cryptography.Pkcs.SignedCms -ArgumentList @($content, $true); " +
            "$signer = New-Object System.Security.Cryptography.Pkcs.CmsSigner($cert); " +
            "$signer.DigestAlgorithm = New-Object System.Security.Cryptography.Oid('2.16.840.1.101.3.4.2.1'); " +
            "$cms.ComputeSignature($signer, $false); " +
            $"[IO.File]::WriteAllBytes('{p7sPath}', $cms.Encode())");
        Assert.True(File.Exists(p7sPath), "The fixture must emit a detached .p7s signature.");

        var output = Path.Combine(f.Root, "out", "release-certification.json");
        var result = RunPowerShellRaw(
            $"& '{EndpointPath}' -RepoRoot '{RepoRoot}' -ReleaseRoot '{releaseRoot}' " +
            $"-CandidateManifest '{f.CandidatePath}' -ReceiptDirectory '{f.ReceiptDir}' " +
            "-ApprovedReceiptSignerThumbprint '0000000000000000000000000000000000000000' " +
            "-ApprovedArtifactSignerThumbprint '0000000000000000000000000000000000000000' " +
            $"-OutputPath '{output}'", expectSuccess: false);
        Assert.NotEqual(0, result.ExitCode);
        var doc = JsonDocument.Parse(File.ReadAllText(output));
        Assert.False(doc.RootElement.GetProperty("productionEligible").GetBoolean(),
            "A self-signed receipt can never satisfy the pinned trusted-chain requirement.");
        Assert.False(doc.RootElement.GetProperty("verifiedFacts").GetProperty("ReceiptSignaturesTrusted").GetBoolean(),
            "ReceiptSignaturesTrusted must be false on an untrusted chain — not merely blocked by another fact.");
        var diagnostics = doc.RootElement.GetProperty("receiptDiagnostics").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        var reasons = doc.RootElement.GetProperty("blockingReasons").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        Assert.True(diagnostics.Any(d => d != null && d.Contains("chain", StringComparison.OrdinalIgnoreCase)),
            "Expected a trusted-chain diagnostic; diagnostics: [" + string.Join("; ", diagnostics) +
            "] blockingReasons: [" + string.Join("; ", reasons) + "] stderr: " + result.Error +
            " stdout: " + result.Output);
    }

    /// <summary>Helper must refuse an absent/non-credential thumbprint and never
    /// mint a .p7s.</summary>
    [Fact]
    public void SignerHelper_FailsClosedWithoutCredential()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        f.WriteReceipt("Build");
        var receiptPath = Path.Combine(f.ReceiptDir, "Build.json");
        var result = RunPowerShellRaw(
            $"& '{SignerPath}' -ReceiptPath '{receiptPath}' " +
            "-CertificateThumbprint '0000000000000000000000000000000000000000'", expectSuccess: false);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(receiptPath + ".p7s"), "No .p7s may be created for an absent credential.");
    }

    /// <summary>Helper must never overwrite an existing .p7s (exclusive create).</summary>
    [Fact]
    public void SignerHelper_RefusesToOverwriteExistingSignature()
    {
        using var f = new Fixture();
        f.WriteCandidate();
        f.WriteReceipt("Build");
        var receiptPath = Path.Combine(f.ReceiptDir, "Build.json");
        var p7sPath = receiptPath + ".p7s";
        File.WriteAllBytes(p7sPath, new byte[] { 1, 2, 3 });
        var before = Sha256OfFile(p7sPath);
        var result = RunPowerShellRaw(
            $"& '{SignerPath}' -ReceiptPath '{receiptPath}' " +
            "-CertificateThumbprint '0000000000000000000000000000000000000000'", expectSuccess: false);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(before, Sha256OfFile(p7sPath));
    }

    /// <summary>The guest-audit inventory helper must refuse to run on the host
    /// (disposable-guest guard) before creating any output file.</summary>
    [Fact]
    public void GuestAuditHelper_RefusesOnHostBeforeCreatingFiles()
    {
        using var f = new Fixture();
        var output = Path.Combine(f.Root, "audit", "audit.json");
        var helper = Path.Combine(RepoRoot, "tools", "Get-ForgerEMSGuestAudit.ps1");
        var result = RunPowerShellRaw(
            $"& '{helper}' -DisposableVmId '{Guid.NewGuid()}' -IsolationKind QEMU -OutputPath '{output}'",
            expectSuccess: false);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(output), "No audit output may be created outside the guarded guest.");
    }

    /// <summary>The endpoint output must be created exclusively — reusing a path
    /// (e.g. a stale true record) fails closed.</summary>
    [Fact]
    public void Endpoint_RefusesToOverwriteExistingOutput()
    {
        using var f = new Fixture();
        var releaseRoot = Path.Combine(f.Root, "release");
        Directory.CreateDirectory(releaseRoot);
        var outDir = Path.Combine(f.Root, "out");
        Directory.CreateDirectory(outDir);
        f.WriteCandidate();
        f.WriteAllGates();
        var output = Path.Combine(outDir, "release-certification.json");
        var args =
            $"& '{EndpointPath}' -RepoRoot '{RepoRoot}' -ReleaseRoot '{releaseRoot}' " +
            $"-CandidateManifest '{f.CandidatePath}' -ReceiptDirectory '{f.ReceiptDir}' " +
            "-ApprovedReceiptSignerThumbprint '0000000000000000000000000000000000000000' " +
            $"-OutputPath '{output}'";
        var first = RunPowerShellRaw(args, expectSuccess: false);
        Assert.NotEqual(0, first.ExitCode);
        Assert.True(File.Exists(output), "Endpoint must write its (false) record.");
        var second = RunPowerShellRaw(args, expectSuccess: false);
        Assert.NotEqual(0, second.ExitCode);
        Assert.Contains("already exists", second.Output + second.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Guest-audit helper seam (AST-extracted, no guest guard bypass) ------

    /// <summary>
    /// The audit script's comparison helpers must work on BOTH [ordered]
    /// OrderedDictionary inventory entries (which do not surface keys through
    /// PSObject.Properties) and ConvertFrom-Json PSCustomObject baseline
    /// entries — under StrictMode. Helpers are extracted from the script via
    /// the PowerShell AST so the test exercises the real implementations
    /// without touching the disposable-guest guard.
    /// </summary>
    [Fact]
    public void GuestAuditHelpers_StableKeys_OnOrderedAndJsonEntries()
    {
        var helper = Path.Combine(RepoRoot, "tools", "Get-ForgerEMSGuestAudit.ps1");
        var output = RunPowerShell(
            "Set-StrictMode -Version Latest; " +
            "$ast = [System.Management.Automation.Language.Parser]::ParseFile('" + helper + "', [ref]$null, [ref]$null); " +
            "$funcs = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true) | " +
            "  Where-Object { $_.Name -in @('Get-GaProp','Get-GaStableKey','Get-GaConfigPayload') }; " +
            "Invoke-Expression (($funcs | ForEach-Object { $_.Extent.Text }) -join \"`n\"); " +
            // OrderedDictionary entries (what the inventory actually emits)
            "$svc = [ordered]@{ name='svc1'; pathName='C:\\x.exe'; startMode='Auto'; state='Running' }; " +
            "if ((Get-GaStableKey -Section 'services' -Item $svc) -ne 'svc:svc1') { throw 'svc stable key (ordered) failed' }; " +
            "$task = [ordered]@{ taskPath='\\'; taskName='QA'; state='Ready'; actions=@('a.exe /q','b.exe') }; " +
            "if ((Get-GaStableKey -Section 'scheduledTasks' -Item $task) -ne 'task:\\|QA') { throw 'task stable key failed' }; " +
            "$cfg = Get-GaConfigPayload -Section 'scheduledTasks' -Item $task; " +
            "if ($cfg.actions.Count -ne 2 -or $cfg.actions[0] -ne 'a.exe /q') { throw 'actions array not preserved' }; " +
            // ConvertFrom-Json baseline entries (PSCustomObject shape)
            "$jo = ('{\"name\":\"svc1\",\"pathName\":\"C:\\\\x.exe\",\"startMode\":\"Auto\",\"state\":\"Stopped\"}' | ConvertFrom-Json); " +
            "if ((Get-GaStableKey -Section 'services' -Item $jo) -ne 'svc:svc1') { throw 'svc stable key (json) failed' }; " +
            "$jt = ('{\"taskPath\":\"\\\\\",\"taskName\":\"QA\",\"actions\":[\"a.exe /q\",\"b.exe\"]}' | ConvertFrom-Json); " +
            "$jcfg = Get-GaConfigPayload -Section 'scheduledTasks' -Item $jt; " +
            "if (@($jcfg.actions).Count -ne 2) { throw 'json actions array not preserved' }; " +
            // missing properties must not throw under StrictMode
            "$partial = [ordered]@{ name='svc2' }; " +
            "if ((Get-GaProp $partial 'pathName') -ne $null) { throw 'missing prop must return null' }; " +
            "'AUDIT_HELPERS_OK'");
        Assert.Contains("AUDIT_HELPERS_OK", output, StringComparison.Ordinal);
    }

    /// <summary>A QA-campaign-owned delta (helper task, resume script, media
    /// artifacts) may never produce an automatic EXPECTED-ONLY verdict — every
    /// qaOwnedChanges entry must also land in reviewDrift so clearing it
    /// requires explicit operator attestation.</summary>
    [Fact]
    public void GuestAudit_QaOwnedDeltas_NeverProduceAutomaticPass()
    {
        var helper = File.ReadAllText(Path.Combine(RepoRoot, "tools", "Get-ForgerEMSGuestAudit.ps1"));
        // The qaOwned branch must exist and must unconditionally also add the
        // delta to reviewDrift inside the same branch.
        var qaBranch = System.Text.RegularExpressions.Regex.Match(
            helper, @"if \(\$qaOwned\) \{(?<branch>.*?)\}", System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.True(qaBranch.Success, "Expected a $qaOwned classification branch.");
        Assert.Contains("qaOwnedChanges", qaBranch.Groups["branch"].Value, StringComparison.Ordinal);
        Assert.Contains("reviewDrift", qaBranch.Groups["branch"].Value, StringComparison.Ordinal);
        // EXPECTED-ONLY must remain reachable only when reviewDrift is empty.
        Assert.Contains("$diff.reviewDrift.Count -gt 0", helper, StringComparison.Ordinal);
    }

    /// <summary>Every new/modified release tooling script must parse cleanly —
    /// the campaign targets Windows PowerShell 5.1, where a single unsupported
    /// token silently dead-ends the guest run.</summary>
    [Fact]
    public void All_ReleaseToolingScripts_ParseClean()
    {
        var scripts = new[]
        {
            Path.Combine("tools", "ForgerEMS.ReleaseCertification.psm1"),
            Path.Combine("tools", "Test-ForgerEMSReleaseCertification.ps1"),
            Path.Combine("tools", "Protect-ForgerEMSReleaseReceipt.ps1"),
            Path.Combine("tools", "Get-ForgerEMSGuestAudit.ps1"),
            Path.Combine("tools", "Test-ForgerEMSInstallerLifecycle.ps1"),
            Path.Combine("tools", "build-release.ps1")
        };
        // The committed test must only reference tracked repo files — .verify/
        // evidence paths are gitignored and absent in fresh CI checkouts.
        foreach (var rel in scripts)
        {
            Assert.DoesNotContain(".verify", rel, StringComparison.OrdinalIgnoreCase);
        }
        foreach (var rel in scripts)
        {
            var path = Path.Combine(RepoRoot, rel);
            Assert.True(File.Exists(path), $"Expected script missing: {rel}");
            var result = RunPowerShell(
                "$errs = $null; " +
                "[void][System.Management.Automation.Language.Parser]::ParseFile('" + path + "', [ref]$null, [ref]$errs); " +
                "if ($errs) { ($errs | ForEach-Object { $_.Message }) -join \"; \" } else { 'PARSE_OK' }");
            Assert.Equal("PARSE_OK", result);
        }
    }

    // ---------- Static workflow / builder guard ------------------------------------

    [Fact]
    public void ReleaseWorkflow_IsStagingOnly_AndCannotPublish()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "release.yml"));
        // Staging produces artifacts for later promotion — it may never create a
        // GitHub Release or invoke a publishing action.
        Assert.Contains("upload-artifact", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("signed-release-candidate", workflow, StringComparison.Ordinal);
        // Every artifact role the candidate manifest binds must be staged —
        // promotion downloads these exact bytes, so an omitted role would make
        // certification fail on a missing file.
        foreach (var staged in new[]
        {
            "app/ForgerEMS.exe",                    // Frontend
            "uninstaller/verified-uninstaller.exe"  // Uninstaller
        })
        {
            Assert.Contains(staged, workflow, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("action-gh-release", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Create GitHub Release", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("releases/upload", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PromotionWorkflow_RequiresCertificationBeforePublishing()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "promote-release.yml"));
        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("signed-release-candidate", workflow, StringComparison.Ordinal);
        Assert.Contains("release-gate-receipts", workflow, StringComparison.Ordinal);
        Assert.Contains("FORGEREMS_RECEIPT_SIGNER_THUMBPRINT", workflow, StringComparison.Ordinal);
        Assert.Contains("release-certification.json", workflow, StringComparison.Ordinal);
        Assert.Contains("candidate-certification.json", workflow, StringComparison.Ordinal);
        // gh api + gh run download need actions:read explicitly — granting only
        // contents:write would suppress the default actions permission.
        Assert.Contains("actions: read", workflow, StringComparison.Ordinal);
        var gateIndex = workflow.IndexOf("Test-ForgerEMSReleaseCertification.ps1", StringComparison.Ordinal);
        var releaseIndex = workflow.IndexOf("Create GitHub Release", StringComparison.Ordinal);
        Assert.True(gateIndex > 0, "promote-release.yml must invoke the certification endpoint");
        Assert.True(gateIndex < releaseIndex,
            "the certification gate must run BEFORE 'Create GitHub Release'");
        // The promotion path must never rebuild or re-sign the candidate.
        Assert.DoesNotContain("build-release.ps1", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet build", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dotnet publish", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuilderMetadata_CannotPromoteByLegacyReleaseJson()
    {
        var builder = File.ReadAllText(Path.Combine(RepoRoot, "tools", "build-release.ps1"));
        Assert.Contains("productionEligible = $false", builder, StringComparison.Ordinal);
        // The old signed+clean-tree predicate must be gone — release.json=true can
        // never promote again; only the certification endpoint may do that.
        Assert.DoesNotContain("productionEligible = (-not $UnsignedCandidate", builder, StringComparison.Ordinal);
        Assert.Contains("candidate-certification.json", builder, StringComparison.Ordinal);
        Assert.Contains("schemaVersion = 2", builder, StringComparison.Ordinal);
    }

    // ---------- PowerShell subprocess plumbing (MechanicalRc pattern) ---------------

    private static string LocateRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "ForgerEMS.sln")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static string RunPowerShell(string command) =>
        RunPowerShellRaw(command, expectSuccess: true).Output.Trim();

    private static (int ExitCode, string Output, string Error) RunPowerShellRaw(string command, bool expectSuccess)
    {
        var exe = ResolvePowerShellExe();
        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        if (Path.GetFileName(exe).Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
        }
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(60000);
        if (expectSuccess && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"PowerShell failed with exit {process.ExitCode}: {error}{output}");
        }
        return (process.ExitCode, output, error);
    }

    private static string ResolvePowerShellExe()
    {
        var psHome = Environment.GetEnvironmentVariable("PSHOME");
        if (!string.IsNullOrWhiteSpace(psHome))
        {
            var candidate = Path.Combine(psHome, "powershell.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return "powershell.exe";
    }
}
