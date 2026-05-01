using System;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraApiKeyStoreTests
{
    [Fact]
    public void Mask_DoesNotReturnFullKey()
    {
        var raw = "ghp_123456789012345678901234567890abcdef";
        var masked = KyraApiKeyStore.Mask(raw);
        Assert.NotEqual(raw, masked);
        Assert.DoesNotContain("123456789012345678901234567890", masked, StringComparison.Ordinal);
    }
}
