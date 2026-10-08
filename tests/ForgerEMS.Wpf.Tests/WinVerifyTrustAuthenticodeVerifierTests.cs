using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ForgerEMS.Wpf.Services.Resources;
using Xunit;
using Xunit.Abstractions;

namespace ForgerEMS.Wpf.Tests;

public sealed class WinVerifyTrustAuthenticodeVerifierTests
{
    private readonly ITestOutputHelper _output;

    public WinVerifyTrustAuthenticodeVerifierTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ProviderFlags_MatchSdkHeaderPolicy()
    {
        // wintrust.h (10.0.26100.0): CACHE_ONLY_URL_RETRIEVAL=0x1000,
        // REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT=0x80, SAFER=0x100.
        Assert.Equal(0x1180u, WinVerifyTrustAuthenticodeVerifier.ExpectedProviderFlags);
    }

    [Fact]
    public void KnownSignedWindowsBinary_VerificationStatusRecorded()
    {
        var kernel32 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "kernel32.dll");
        Assert.True(File.Exists(kernel32), "Test requires a Windows System32 signed binary.");

        var result = new WinVerifyTrustAuthenticodeVerifier().Verify(kernel32, null);
        _output.WriteLine($"kernel32.dll: valid={result.IsValid} signer='{result.SignerSubject}' reason='{result.Failure}'");

        // Record the real status. If the signature chain verified, signer identity must be populated;
        // a failure (e.g. cached revocation data unavailable) must carry an explicit reason, never a
        // silent pass or an empty explanation.
        if (result.IsValid)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.SignerSubject));
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Failure));
        }
    }

    [Fact]
    public void KnownSignedWindowsBinary_WrongPublisher_RejectedWhenSignatureValid()
    {
        var kernel32 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "kernel32.dll");
        Assert.True(File.Exists(kernel32));

        var verifier = new WinVerifyTrustAuthenticodeVerifier();
        var baseResult = verifier.Verify(kernel32, null);
        if (!baseResult.IsValid)
        {
            _output.WriteLine($"Signature verification unavailable on this host ({baseResult.Failure}); publisher-match check not reached.");
            return;
        }

        var result = verifier.Verify(kernel32, "Contoso Not Microsoft Ltd");
        Assert.False(result.IsValid);
        Assert.Contains("does not exactly match", result.Failure, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    // expected null/blank -> pinning disabled, any signer accepted
    [InlineData("Microsoft Corporation", null, true)]
    [InlineData("Microsoft Corporation", "", true)]
    [InlineData("Microsoft Corporation", "   ", true)]
    [InlineData(null, null, true)]
    [InlineData(null, "", true)]
    // exact match, case-insensitive
    [InlineData("Forger Digital Solutions", "Forger Digital Solutions", true)]
    [InlineData("FORGER DIGITAL SOLUTIONS", "forger digital solutions", true)]
    // expected set -> only exact matches pass
    [InlineData("Forger Digital Solutions Ltd", "Forger Digital Solutions", false)] // suffix
    [InlineData("Evil Forger Digital Solutions", "Forger Digital Solutions", false)] // prefix
    [InlineData("xForger Digital Solutionsx", "Forger Digital Solutions", false)] // substring
    [InlineData("", "Forger Digital Solutions", false)] // blank actual
    [InlineData("   ", "Forger Digital Solutions", false)] // whitespace actual
    [InlineData(null, "Forger Digital Solutions", false)] // null actual
    [InlineData("Other Publisher", "Forger Digital Solutions", false)]
    public void PublisherMatches_ExactCaseInsensitiveOnlyWhenPinned(string? signer, string? expected, bool expectedResult)
    {
        Assert.Equal(expectedResult, WinVerifyTrustAuthenticodeVerifier.PublisherMatches(signer, expected));
    }

    [Fact]
    public void Verify_UsesExtractedPublisherMatches()
    {
        var text = File.ReadAllText(FindRepoFile("Services", "Resources", "WinVerifyTrustAuthenticodeVerifier.cs"));
        Assert.Contains("PublisherMatches(signer, expectedPublisher)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsignedFile_Rejected()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"unsigned-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(temp, new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0xDE, 0xAD });
            var result = new WinVerifyTrustAuthenticodeVerifier().Verify(temp, null);
            Assert.False(result.IsValid);
            Assert.False(string.IsNullOrWhiteSpace(result.Failure));
            _output.WriteLine($"unsigned fixture: reason='{result.Failure}'");
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void NativeInteropStructLayout_MatchesX64SdkExpectations()
    {
        // x64 layout contract vs wintrust.h (10.0.26100.0) — guard against a field
        // reorder/regression silently corrupting the P/Invoke signature.
        Assert.Equal(8, IntPtr.Size); // win-x64 test host only

        var fileInfo = typeof(WinVerifyTrustAuthenticodeVerifier)
            .GetNestedType("WINTRUST_FILE_INFO", BindingFlags.NonPublic);
        Assert.NotNull(fileInfo);
        Assert.Equal(32, Marshal.SizeOf(fileInfo!));
        Assert.Equal(0, (int)Marshal.OffsetOf(fileInfo!, "cbStruct"));
        Assert.Equal(8, (int)Marshal.OffsetOf(fileInfo!, "pcwszFilePath"));
        Assert.Equal(16, (int)Marshal.OffsetOf(fileInfo!, "hFile"));
        Assert.Equal(24, (int)Marshal.OffsetOf(fileInfo!, "pgKnownSubject"));

        var data = typeof(WinVerifyTrustAuthenticodeVerifier)
            .GetNestedType("WINTRUST_DATA", BindingFlags.NonPublic);
        Assert.NotNull(data);
        Assert.Equal(80, Marshal.SizeOf(data!));
        Assert.Equal(40, (int)Marshal.OffsetOf(data!, "pFile"));
        Assert.Equal(48, (int)Marshal.OffsetOf(data!, "dwStateAction"));
        Assert.Equal(56, (int)Marshal.OffsetOf(data!, "hWVTStateData"));
        Assert.Equal(64, (int)Marshal.OffsetOf(data!, "pwszURLReference"));
        Assert.Equal(72, (int)Marshal.OffsetOf(data!, "dwProvFlags"));
    }

    private static string FindRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName, "src", "ForgerEMS.Wpf" }.Concat(segments).ToArray());
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate repo path.", Path.Combine(segments));
    }
}
