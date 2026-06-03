using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Filename-aware checksum resolver tests. Exercises the PowerShell helper
/// (<c>backend/ToolkitManager/ChecksumResolver.ps1</c>) end-to-end so the
/// vendor checksum-file shapes ForgerEMS will see at runtime are validated
/// against the same code that runs in production.
/// </summary>
public sealed class ChecksumResolverTests
{
    [Fact]
    public void Resolver_GnuFormat_PicksHashByTargetFilename()
    {
        const string content = """
abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234  other.iso
0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  alpine-standard-3.20.0-x86_64.iso
1111222233334444555566667777888899990000aaaabbbbccccddddeeeeffff  *another.iso
""";
        var result = ResolveContent(content, targetFileName: "alpine-standard-3.20.0-x86_64.iso");
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", result.Hash);
        Assert.Equal("Matched", result.Reason);
        Assert.Equal("GNU", result.SourceFormat);
        Assert.Equal(1, result.Candidates);
    }

    [Fact]
    public void Resolver_BsdFormat_PicksHashByTargetFilename()
    {
        const string content = """
SHA256 (FreeBSD-14.1-RELEASE-amd64-disc1.iso) = aaaabbbbccccddddaaaabbbbccccddddaaaabbbbccccddddaaaabbbbccccdddd
SHA256 (FreeBSD-14.1-RELEASE-amd64-bootonly.iso) = 9999888877776666999988887777666699998888777766669999888877776666
SHA512 (FreeBSD-14.1-RELEASE-amd64-disc1.iso) = ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff
""";
        var result = ResolveContent(content, targetFileName: "FreeBSD-14.1-RELEASE-amd64-disc1.iso");
        Assert.Equal("aaaabbbbccccddddaaaabbbbccccddddaaaabbbbccccddddaaaabbbbccccdddd", result.Hash);
        Assert.Equal("Matched", result.Reason);
        Assert.Equal("BSD", result.SourceFormat);
    }

    [Fact]
    public void Resolver_BsdNoSpaceFormat_PicksHashByTargetFilename()
    {
        // Wireshark SIGNATURES-X.Y.Z.txt shape: `SHA256(filename)=hash` (no space
        // before the opening paren, no space around `=`). The resolver must
        // match this without requiring whitespace and must skip the SHA1 line.
        const string content = """
Wireshark-4.6.6-x64.exe (102937440 bytes)
SHA1(Wireshark-4.6.6-x64.exe)=c6180a36f0461342092c63806350a6ebbfa587af
SHA256(Wireshark-4.6.6-x64.exe)=ab28d13695ace992307fb1aaea7144f977d5b6562acec1f465c9c0a9fa04190c
SHA256(Wireshark-4.6.6-x64.msi)=90104f6eae9b1fb4b1c8ebf6313d73e42f34ee274d3bc26bdc86741d47466c9d
""";
        var result = ResolveContent(content, targetFileName: "Wireshark-4.6.6-x64.exe");
        Assert.Equal("ab28d13695ace992307fb1aaea7144f977d5b6562acec1f465c9c0a9fa04190c", result.Hash);
        Assert.Equal("Matched", result.Reason);
        Assert.Equal("BSD", result.SourceFormat);
        Assert.Equal(1, result.Candidates);
    }

    [Fact]
    public void Resolver_BareHashFile_ReturnsHashWithoutTarget()
    {
        const string content = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var result = ResolveContent(content, targetFileName: string.Empty);
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", result.Hash);
        Assert.Equal("NoTarget", result.Reason);
    }

    [Fact]
    public void Resolver_GitHubAssetDigestJson_ReturnsSingleDigestWithTarget()
    {
        const string content = """
{
  "name": "VeraCrypt.Setup.1.26.24.exe",
  "digest": "sha256:08b80ab6a6c4eca08e18096c9468fe0bd2e33fc23142730e59177e6fcd7c902d"
}
""";
        var result = ResolveContent(content, targetFileName: "VeraCrypt.Setup.1.26.24.exe");
        Assert.Equal("08b80ab6a6c4eca08e18096c9468fe0bd2e33fc23142730e59177e6fcd7c902d", result.Hash);
        Assert.Equal("Matched", result.Reason);
        Assert.Equal("GitHubDigest", result.SourceFormat);
        Assert.Equal(1, result.Candidates);
    }

    [Fact]
    public void Resolver_SingleGnuLine_ResolvesWithoutTarget()
    {
        const string content = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  alpine-standard-3.20.0-x86_64.iso";
        var result = ResolveContent(content, targetFileName: string.Empty);
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", result.Hash);
        Assert.Equal("NoTarget", result.Reason);
    }

    [Fact]
    public void Resolver_MultiLineWithoutTarget_RefusesToGuess()
    {
        const string content = """
abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234  other.iso
0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  alpine.iso
""";
        var result = ResolveContent(content, targetFileName: string.Empty);
        Assert.Equal(string.Empty, result.Hash);
        Assert.Equal("AmbiguousNoTarget", result.Reason);
    }

    [Fact]
    public void Resolver_TargetNotFound_ReturnsExplicitReason()
    {
        const string content = """
abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234  other.iso
0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  alpine.iso
""";
        var result = ResolveContent(content, targetFileName: "missing.iso");
        Assert.Equal(string.Empty, result.Hash);
        Assert.Equal("TargetNotFound", result.Reason);
    }

    [Fact]
    public void Resolver_DuplicateFilenameDifferentHashes_FlagsAmbiguous()
    {
        const string content = """
aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  foo.iso
bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  foo.iso
""";
        var result = ResolveContent(content, targetFileName: "foo.iso");
        Assert.Equal(string.Empty, result.Hash);
        Assert.Equal("Ambiguous", result.Reason);
        Assert.Equal(2, result.Candidates);
    }

    [Fact]
    public void Resolver_DuplicateFilenameSameHash_AcceptsMatch()
    {
        const string content = """
aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  foo.iso
aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  foo.iso
""";
        var result = ResolveContent(content, targetFileName: "foo.iso");
        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.Hash);
        Assert.Equal("Matched", result.Reason);
    }

    [Fact]
    public void Resolver_StarPrefix_BinaryModeFilenameMatches()
    {
        const string content = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  *alpine.iso";
        var result = ResolveContent(content, targetFileName: "alpine.iso");
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", result.Hash);
        Assert.Equal("Matched", result.Reason);
    }

    [Fact]
    public void Resolver_LeadingDotSlash_StrippedBeforeMatch()
    {
        const string content = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  ./subdir/alpine.iso";
        var result = ResolveContent(content, targetFileName: "alpine.iso");
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", result.Hash);
        Assert.Equal("Matched", result.Reason);
    }

    [Fact]
    public void Resolver_TabSeparator_Tolerated()
    {
        const string content = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\t*alpine.iso";
        var result = ResolveContent(content, targetFileName: "alpine.iso");
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", result.Hash);
        Assert.Equal("Matched", result.Reason);
    }

    [Fact]
    public void Resolver_CaseInsensitiveFilename_Matches()
    {
        const string content = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  ALPINE.ISO";
        var result = ResolveContent(content, targetFileName: "alpine.iso");
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", result.Hash);
        Assert.Equal("Matched", result.Reason);
    }

    [Fact]
    public void Resolver_Sha512LinesIgnored_WhenSha256AlsoPresent()
    {
        const string content = """
SHA512 (foo.iso) = ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff
0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  foo.iso
""";
        var result = ResolveContent(content, targetFileName: "foo.iso");
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", result.Hash);
        Assert.Equal("Matched", result.Reason);
    }

    [Fact]
    public void Resolver_OnlySha512Lines_ReturnsEmptyHash()
    {
        const string content = "SHA512 (foo.iso) = ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
        var result = ResolveContent(content, targetFileName: "foo.iso");
        Assert.Equal(string.Empty, result.Hash);
        Assert.Equal("NoSha256Lines", result.Reason);
    }

    [Fact]
    public void Resolver_Sha512BsdFormat_PicksHashByTargetFilename_WhenExplicitlyRequested()
    {
        const string content = """
SHA512 (NetBSD-10.1-alpha.iso) = 0f69e7b4f71325f9d6946a8d2481b61fec8dcb07f1a38c1cd4533763d27b699569d3954b416823cc8e8d2a3c18983af5df1abd11b076278b3c3cf7be3a6432d7
SHA512 (NetBSD-10.1-amd64.iso) = 7a5e5071307e1795885ffc6e1b8aac465082c21c8b79f4c9b4103ef44e4f2da45477299d213ae0093f6534dc99dc2bbf78f41e9dd556c72a02516068bf43fe49
SHA256 (NetBSD-10.1-amd64.iso) = aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
""";
        var result = ResolveContent(content, targetFileName: "NetBSD-10.1-amd64.iso", algorithm: "SHA512");
        Assert.Equal("7a5e5071307e1795885ffc6e1b8aac465082c21c8b79f4c9b4103ef44e4f2da45477299d213ae0093f6534dc99dc2bbf78f41e9dd556c72a02516068bf43fe49", result.Hash);
        Assert.Equal("Matched", result.Reason);
        Assert.Equal("BSD", result.SourceFormat);
        Assert.Equal("SHA512", result.Algorithm);
    }

    [Fact]
    public void Resolver_Sha512DuplicateFilenameDifferentHashes_FlagsAmbiguous()
    {
        const string content = """
aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  foo.iso
bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  foo.iso
""";
        var result = ResolveContent(content, targetFileName: "foo.iso", algorithm: "SHA512");
        Assert.Equal(string.Empty, result.Hash);
        Assert.Equal("Ambiguous", result.Reason);
        Assert.Equal(2, result.Candidates);
    }

    [Fact]
    public void Resolver_Sha512MalformedLines_ReturnTargetNotFound()
    {
        const string content = """
SHA512 (foo.iso) = not-a-hash
SHA512 (bar.iso) = 0123
MD5 (foo.iso) = d41d8cd98f00b204e9800998ecf8427e
""";
        var result = ResolveContent(content, targetFileName: "foo.iso", algorithm: "SHA512");
        Assert.Equal(string.Empty, result.Hash);
        Assert.Equal("NoSha512Lines", result.Reason);
    }

    [Fact]
    public void Resolver_Sha512MultiLineWithoutTarget_RefusesToGuess()
    {
        const string content = """
aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  one.iso
bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  two.iso
""";
        var result = ResolveContent(content, targetFileName: string.Empty, algorithm: "SHA512");
        Assert.Equal(string.Empty, result.Hash);
        Assert.Equal("AmbiguousNoTarget", result.Reason);
    }

    [Fact]
    public void GetSha512FromSourceUrl_LocalFile_MultiLine_ResolvesByTargetFilename()
    {
        var root = CreateTempRoot();
        try
        {
            var checksumPath = Path.Combine(root, "SHA512");
            File.WriteAllText(checksumPath,
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  other.iso" + Environment.NewLine +
                "7a5e5071307e1795885ffc6e1b8aac465082c21c8b79f4c9b4103ef44e4f2da45477299d213ae0093f6534dc99dc2bbf78f41e9dd556c72a02516068bf43fe49  NetBSD-10.1-amd64.iso");

            var script = @"
. '<RESOLVER>'
$result = Get-Sha512FromSourceUrl -ShaUrl '<CSUM>' -TargetFileName 'NetBSD-10.1-amd64.iso'
Write-Output $result
".Replace("<RESOLVER>", FindRepoFile("backend", "ToolkitManager", "ChecksumResolver.ps1"))
              .Replace("<CSUM>", checksumPath);

            var stdout = RunPowerShellSnippet(script);
            Assert.Equal("7a5e5071307e1795885ffc6e1b8aac465082c21c8b79f4c9b4103ef44e4f2da45477299d213ae0093f6534dc99dc2bbf78f41e9dd556c72a02516068bf43fe49", stdout.Trim());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolver_MalformedLines_AreSkippedNotFatal()
    {
        const string content = """
this is not a checksum line
zzzz nothing here
# comment line that should be skipped
0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  target.iso
""";
        var result = ResolveContent(content, targetFileName: "target.iso");
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", result.Hash);
        Assert.Equal("Matched", result.Reason);
    }

    [Fact]
    public void Resolver_EmptyContent_ReturnsEmptyHash()
    {
        var result = ResolveContent(string.Empty, targetFileName: string.Empty);
        Assert.Equal(string.Empty, result.Hash);
        Assert.Equal("EmptyContent", result.Reason);
    }

    [Fact]
    public void Resolver_Utf8Bom_Tolerated()
    {
        var content = "﻿0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var result = ResolveContent(content, targetFileName: string.Empty);
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", result.Hash);
        Assert.Equal("NoTarget", result.Reason);
    }

    [Fact]
    public void Resolver_MixedExtensions_BasenameMatchSucceeds()
    {
        // Real-world example: vendors sometimes list filenames with full relative paths.
        const string content = """
0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  releases/x86_64/alpine-standard-3.20.0-x86_64.iso
1111111111111111111111111111111111111111111111111111111111111111  releases/x86_64/alpine-extended-3.20.0-x86_64.iso
""";
        var result = ResolveContent(content, targetFileName: "alpine-standard-3.20.0-x86_64.iso");
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", result.Hash);
        Assert.Equal("Matched", result.Reason);
    }

    [Fact]
    public void GetSha256FromSourceUrl_LocalFile_MultiLine_ResolvesByTargetFilename()
    {
        var root = CreateTempRoot();
        try
        {
            var checksumPath = Path.Combine(root, "SHA256SUMS");
            File.WriteAllText(checksumPath,
                "abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234  other.iso" + Environment.NewLine +
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  alpine.iso");

            var script = @"
. '<RESOLVER>'
$result = Get-Sha256FromSourceUrl -ShaUrl '<CSUM>' -TargetFileName 'alpine.iso'
Write-Output $result
".Replace("<RESOLVER>", FindRepoFile("backend", "ToolkitManager", "ChecksumResolver.ps1"))
              .Replace("<CSUM>", checksumPath);

            var stdout = RunPowerShellSnippet(script);
            Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", stdout.Trim());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GetSha256FromSourceUrl_LocalFile_NoTargetWhenMultiLine_ReturnsEmpty()
    {
        var root = CreateTempRoot();
        try
        {
            var checksumPath = Path.Combine(root, "SHA256SUMS");
            File.WriteAllText(checksumPath,
                "abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234  other.iso" + Environment.NewLine +
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  alpine.iso");

            var script = @"
. '<RESOLVER>'
$result = Get-Sha256FromSourceUrl -ShaUrl '<CSUM>'
if ([string]::IsNullOrEmpty($result)) { Write-Output 'EMPTY' } else { Write-Output $result }
".Replace("<RESOLVER>", FindRepoFile("backend", "ToolkitManager", "ChecksumResolver.ps1"))
              .Replace("<CSUM>", checksumPath);

            var stdout = RunPowerShellSnippet(script);
            Assert.Equal("EMPTY", stdout.Trim());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ConvertChecksumResponseContentToString_ByteArray_DecodesUtf8ChecksumText()
    {
        var script = @"
. '<RESOLVER>'
$bytes = [Text.Encoding]::UTF8.GetBytes('0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  alpine.iso')
$result = Convert-ChecksumResponseContentToString -Content $bytes
Write-Output $result
".Replace("<RESOLVER>", FindRepoFile("backend", "ToolkitManager", "ChecksumResolver.ps1"));

        var stdout = RunPowerShellSnippet(script);
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  alpine.iso", stdout.Trim());
    }

    private static ResolverResult ResolveContent(string content, string targetFileName, string algorithm = "SHA256")
    {
        var root = CreateTempRoot();
        try
        {
            var contentPath = Path.Combine(root, "content.txt");
            File.WriteAllText(contentPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var script = @"
. '<RESOLVER>'
$raw = [System.IO.File]::ReadAllText('<CONTENT>')
$result = Resolve-ChecksumFromChecksumText -Content $raw -TargetFileName '<TARGET>' -Algorithm '<ALGORITHM>'
Write-Output ('HASH=' + $result.Hash)
Write-Output ('REASON=' + $result.Reason)
Write-Output ('CANDIDATES=' + $result.Candidates)
Write-Output ('FORMAT=' + $result.SourceFormat)
Write-Output ('ALGORITHM=' + $result.Algorithm)
".Replace("<RESOLVER>", FindRepoFile("backend", "ToolkitManager", "ChecksumResolver.ps1"))
              .Replace("<CONTENT>", contentPath)
              .Replace("<TARGET>", targetFileName.Replace("'", "''"))
              .Replace("<ALGORITHM>", algorithm);

            var stdout = RunPowerShellSnippet(script);
            var parsed = new ResolverResult();
            foreach (var line in stdout.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r').Trim();
                if (trimmed.StartsWith("HASH=", StringComparison.Ordinal)) { parsed.Hash = trimmed["HASH=".Length..]; }
                else if (trimmed.StartsWith("REASON=", StringComparison.Ordinal)) { parsed.Reason = trimmed["REASON=".Length..]; }
                else if (trimmed.StartsWith("CANDIDATES=", StringComparison.Ordinal))
                {
                    parsed.Candidates = int.TryParse(trimmed["CANDIDATES=".Length..], out var c) ? c : 0;
                }
                else if (trimmed.StartsWith("FORMAT=", StringComparison.Ordinal)) { parsed.SourceFormat = trimmed["FORMAT=".Length..]; }
                else if (trimmed.StartsWith("ALGORITHM=", StringComparison.Ordinal)) { parsed.Algorithm = trimmed["ALGORITHM=".Length..]; }
            }

            return parsed;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string RunPowerShellSnippet(string script)
    {
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
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("PowerShell did not start.");
        Assert.True(process.WaitForExit(30_000), "PowerShell timed out.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, stdout + Environment.NewLine + stderr);
        return stdout;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "forgerems-resolver-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
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

    private sealed class ResolverResult
    {
        public string Hash { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public int Candidates { get; set; }
        public string SourceFormat { get; set; } = string.Empty;
        public string Algorithm { get; set; } = string.Empty;
    }
}
