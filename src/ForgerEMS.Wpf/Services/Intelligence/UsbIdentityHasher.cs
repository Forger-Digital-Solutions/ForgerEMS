using System;
using System.Security.Cryptography;
using System.Text;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

/// <summary>Produces stable, non-reversible hashes for USB identity fields. Never place raw PNP/serial in Kyra or public JSON.</summary>
public static class UsbIdentityHasher
{
    public static string Sha256Hex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var norm = value.Trim();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(norm));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>16-char prefix for UI / correlation (still not reversible to raw).</summary>
    public static string ShortKey(string? fullHex)
    {
        if (string.IsNullOrWhiteSpace(fullHex))
        {
            return "—";
        }

        return fullHex.Length <= 16 ? fullHex : fullHex[..16];
    }
}
