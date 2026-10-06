using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace ForgerEMS.Wpf.Services.Resources;

/// <summary>
/// Authenticode verification via WinVerifyTrust (generic verify action = system chain
/// policy) plus signer identity from X509Certificate.CreateFromSignedFile.
/// Native imports are pinned to System32 so no search-path DLL can be substituted.
/// </summary>
public sealed class WinVerifyTrustAuthenticodeVerifier : IAuthenticodeVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    // Constants verified against installed SDK wintrust.h (10.0.26100.0).
    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_WHOLECHAIN = 1;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_VERIFY = 0x00000001;
    private const uint WTD_STATEACTION_CLOSE = 0x00000002;
    // Use only cached CRL/AIA data — offline revocation is fail-closed, never silently skipped.
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;
    private const uint WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT = 0x00000080;
    private const uint WTD_SAFER_FLAG = 0x00000100;
    internal const uint ExpectedProviderFlags =
        WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT | WTD_SAFER_FLAG;

    private static readonly IntPtr InvalidHandle = new(-1);

    public AuthenticodeResult Verify(string filePath, string? expectedPublisher)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new AuthenticodeResult(false, null, "Authenticode verification is only available on Windows.");
        }

        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero
        };

        var data = new WINTRUST_DATA
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
            pPolicyCallbackData = IntPtr.Zero,
            pSIPClientData = IntPtr.Zero,
            dwUIChoice = WTD_UI_NONE,
            fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN,
            dwUnionChoice = WTD_CHOICE_FILE,
            pFile = IntPtr.Zero,
            dwStateAction = WTD_STATEACTION_VERIFY,
            hWVTStateData = IntPtr.Zero,
            pwszURLReference = IntPtr.Zero,
            dwProvFlags = ExpectedProviderFlags,
            dwUIContext = 0
        };

        data.pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        Marshal.StructureToPtr(fileInfo, data.pFile, false);

        var status = -1;
        try
        {
            var guid = GenericVerifyV2;
            status = WinVerifyTrust(InvalidHandle, ref guid, ref data);
        }
        catch (Exception ex)
        {
            return new AuthenticodeResult(false, null, $"WinVerifyTrust invocation failed: {ex.Message}");
        }
        finally
        {
            // Close the state with the same valid structure and the Close action.
            if (data.hWVTStateData != IntPtr.Zero)
            {
                var close = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                    dwUIChoice = WTD_UI_NONE,
                    dwUnionChoice = WTD_CHOICE_FILE,
                    pFile = data.pFile,
                    dwStateAction = WTD_STATEACTION_CLOSE,
                    hWVTStateData = data.hWVTStateData,
                    dwProvFlags = ExpectedProviderFlags
                };
                var guid = GenericVerifyV2;
                try
                {
                    _ = WinVerifyTrust(InvalidHandle, ref guid, ref close);
                }
                catch
                {
                    // best-effort state close
                }
            }

            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(data.pFile);
            Marshal.FreeHGlobal(data.pFile);
        }

        if (status != 0)
        {
            var reason = status switch
            {
                unchecked((int)0x800B0100) => "No signature present or signature is invalid (TRUST_E_NOSIGNATURE).",
                unchecked((int)0x80096010) => "Signature did not verify against file content (TRUST_E_BAD_DIGEST).",
                unchecked((int)0x800B010A) => "Certificate chain could not be built to a trusted root (CERT_E_CHAINING).",
                unchecked((int)0x800B0101) => "Certificate is expired or not yet valid (CERT_E_EXPIRED).",
                unchecked((int)0x800B0109) => "Chain terminated in an untrusted root (CERT_E_UNTRUSTEDROOT).",
                unchecked((int)0x80092026) => "Revocation status could not be verified (fail-closed policy).",
                _ => $"WinVerifyTrust failed with status 0x{status:X8}."
            };
            return new AuthenticodeResult(false, null, reason);
        }

        // Signer identity from the certificate blob — exact SimpleName match, not substring.
        string? signer = null;
        try
        {
            using var raw = X509Certificate.CreateFromSignedFile(filePath);
            using var cert = new X509Certificate2(raw);
            signer = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        }
        catch (Exception ex)
        {
            return new AuthenticodeResult(false, null,
                $"WinVerifyTrust passed but signer certificate could not be read: {ex.Message}");
        }

        if (!string.IsNullOrWhiteSpace(expectedPublisher)
            && !string.Equals(signer, expectedPublisher, StringComparison.OrdinalIgnoreCase))
        {
            return new AuthenticodeResult(
                false, signer,
                $"Signer '{signer ?? "unknown"}' does not exactly match expected publisher '{expectedPublisher}'.");
        }

        return new AuthenticodeResult(true, signer, null);
    }

    [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
    }
}
