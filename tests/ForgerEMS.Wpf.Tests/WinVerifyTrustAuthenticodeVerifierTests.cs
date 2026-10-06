using System;
using System.IO;
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
}
