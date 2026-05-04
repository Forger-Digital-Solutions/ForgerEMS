using System;
using VentoyToolkitSetup.Wpf.Configuration;

namespace VentoyToolkitSetup.Wpf.Services.Licensing;

/// <summary>Central place for entitlement checks. Public Preview keeps current capabilities unlocked; stricter gates apply for <see cref="LicenseTier.Free"/> only.</summary>
public static class FeatureGateService
{
    public static LicenseTier ResolveEffectiveTier(bool betaSettingsEntitlementFlag)
    {
        var raw = ForgerEmsEnvironmentConfiguration.LicenseTierRaw;
        if (string.Equals(raw, "Developer", StringComparison.OrdinalIgnoreCase))
        {
            return LicenseTier.Developer;
        }

        if (string.Equals(raw, "Pro", StringComparison.OrdinalIgnoreCase))
        {
            return LicenseTier.Pro;
        }

        if (string.Equals(raw, "BetaTesterPro", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "BetaTester", StringComparison.OrdinalIgnoreCase))
        {
            return LicenseTier.BetaTesterPro;
        }

        if (string.Equals(raw, "Free", StringComparison.OrdinalIgnoreCase))
        {
            return LicenseTier.Free;
        }

        if (betaSettingsEntitlementFlag)
        {
            return LicenseTier.BetaTesterPro;
        }

        return LicenseTier.PublicPreview;
    }

    /// <summary>USB Intelligence / port mapping — Pro Preview label; remains available during Public Preview and BetaTesterPro.</summary>
    public static bool IsUsbIntelligenceExperienceEnabled(LicenseTier tier) =>
        tier is not LicenseTier.Free;

    public static bool IsAdvancedKyraProviderConfigurationHighlighted(LicenseTier tier) =>
        tier is LicenseTier.BetaTesterPro or LicenseTier.Pro or LicenseTier.Developer;

    public static bool IsMarketplaceValuationStubVisible(LicenseTier tier) =>
        tier != LicenseTier.Free && ForgerEmsFeatureFlags.MarketplaceEnabled;
}
