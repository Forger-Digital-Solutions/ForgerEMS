using System;
using System.Security.Cryptography;
using System.Text;

namespace VentoyToolkitSetup.Wpf.Services.DriveValidation;

internal static class DriveValidationSignature
{
    public const int HeaderBytes = 16;

    public static byte[] BuildBlock(int sampleIndex, int blockIndex, int seed, int blockSize)
    {
        var buffer = new byte[blockSize];
        var header = Encoding.UTF8.GetBytes($"FGDV|{sampleIndex:D4}|{blockIndex:D4}|{seed:D8}");
        var copyLen = Math.Min(HeaderBytes, buffer.Length);
        Array.Copy(header, 0, buffer, 0, copyLen);

        var rng = new Random(unchecked(sampleIndex * 397 + blockIndex * 911 + seed * 17));
        for (var i = HeaderBytes; i < buffer.Length; i++)
        {
            buffer[i] = (byte)rng.Next(0, 256);
        }

        return buffer;
    }

    public static string ComputeHex(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }

    public static bool VerifyBlock(byte[] actual, int sampleIndex, int blockIndex, int seed)
    {
        if (actual.Length < HeaderBytes)
        {
            return false;
        }

        var expectedHeader = Encoding.UTF8.GetBytes($"FGDV|{sampleIndex:D4}|{blockIndex:D4}|{seed:D8}");
        for (var i = 0; i < HeaderBytes; i++)
        {
            if (actual[i] != expectedHeader[i])
            {
                return false;
            }
        }

        var expected = BuildBlock(sampleIndex, blockIndex, seed, actual.Length);
        for (var i = HeaderBytes; i < actual.Length; i++)
        {
            if (actual[i] != expected[i])
            {
                return false;
            }
        }

        return true;
    }

    public static bool BlocksAppearAliased(byte[] firstSampleHead, byte[] lateSampleHead)
    {
        if (firstSampleHead.Length < HeaderBytes || lateSampleHead.Length < HeaderBytes)
        {
            return false;
        }

        var compareLen = Math.Min(512, Math.Min(firstSampleHead.Length, lateSampleHead.Length));
        var equal = true;
        for (var i = 0; i < compareLen; i++)
        {
            if (firstSampleHead[i] != lateSampleHead[i])
            {
                equal = false;
                break;
            }
        }

        return equal;
    }

    /// <summary>
    /// Counts pairs of sample heads that look identical. A counterfeit drive with overlapping
    /// physical regions may report a successful write/read but every sample is actually pointing
    /// at the same address space, so two distinct expected signatures end up reading identical
    /// bytes — that is the signal we want.
    /// </summary>
    public static int CountAliasedHeadPairs(System.Collections.Generic.IReadOnlyList<byte[]> heads)
    {
        var pairs = 0;
        for (var i = 0; i < heads.Count; i++)
        {
            for (var j = i + 1; j < heads.Count; j++)
            {
                if (BlocksAppearAliased(heads[i], heads[j]))
                {
                    pairs++;
                }
            }
        }

        return pairs;
    }

    /// <summary>
    /// Returns true if the block buffer is suspicious — all zero or all 0xFF over the verifiable
    /// region. Real signature blocks should never read back as a uniform pattern.
    /// </summary>
    public static bool IsUniformPattern(byte[] buffer, int length)
    {
        if (length < HeaderBytes)
        {
            return false;
        }

        var first = buffer[0];
        if (first != 0x00 && first != 0xFF)
        {
            return false;
        }

        for (var i = 1; i < length; i++)
        {
            if (buffer[i] != first)
            {
                return false;
            }
        }

        return true;
    }
}
