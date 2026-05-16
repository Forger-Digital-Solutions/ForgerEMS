#pragma warning disable CA1305 // Locale-sensitive calls; text is diagnostic/UI output
// FORGEREMS_KYRA_ADAPTER: ForgerEMS-specific coupling; stays in ForgerEMS.KyraAdapter.
// Reads HKLM\\Software\\ForgerEMS registry; pure ForgerEMS installer integration.
using System;
using Microsoft.Win32;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Configuration;

/// <summary>
/// Reads optional Kyra Intelligence consent flags written by the Inno Setup installer
/// under HKLM\Software\ForgerEMS (same root as Deep Sensor installer defaults).
/// Applied only when copilot-settings.json did not exist before load (first run for that profile).
/// </summary>
public static class KyraInstallerIntelligenceRegistry
{
    public const string SubKeyName = @"Software\ForgerEMS";

    public const string ValueShareRepairIntelligence = "KyraShareRepairIntelligence";

    public const string ValueShareHardwarePatterns = "KyraShareHardwarePatterns";

    public const string ValueShareResolvedCategories = "KyraShareResolvedCategories";

    public const string ValueShareCrashDiagnostics = "KyraShareCrashDiagnostics";

    public readonly record struct InstallerKyraConsentSnapshot(int Repair, int Hardware, int Resolved, int Crash)
    {
        public bool Any => Repair != 0 || Hardware != 0 || Resolved != 0 || Crash != 0;
    }

    public static InstallerKyraConsentSnapshot ReadSnapshot() =>
        new(
            ReadDword0Or1(ValueShareRepairIntelligence),
            ReadDword0Or1(ValueShareHardwarePatterns),
            ReadDword0Or1(ValueShareResolvedCategories),
            ReadDword0Or1(ValueShareCrashDiagnostics));

    public static void ApplySnapshotToSettings(CopilotSettings settings, InstallerKyraConsentSnapshot snap)
    {
        var any = snap.Any;
        settings.KyraCommunitySharingEnabled = any;
        settings.KyraShareHardwareCompatibilityPerformancePatterns = any && snap.Hardware != 0;
        settings.KyraShareResolvedIssueFixPatterns = any && snap.Resolved != 0;
        settings.KyraShareCrashErrorDiagnostics = any && snap.Crash != 0;
    }

    public static void ApplyWhenNewConfig(CopilotSettings settings, bool copilotSettingsFileExistedBeforeLoad)
    {
        if (copilotSettingsFileExistedBeforeLoad)
        {
            return;
        }

        ApplySnapshotToSettings(settings, ReadSnapshot());
    }

    private static int ReadDword0Or1(string valueName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SubKeyName, writable: false);
            var o = key?.GetValue(valueName);
            if (o is int i)
            {
                return i != 0 ? 1 : 0;
            }

            if (o is long l)
            {
                return l != 0 ? 1 : 0;
            }

            if (int.TryParse(Convert.ToString(o), out var p))
            {
                return p != 0 ? 1 : 0;
            }
        }
        catch
        {
            // missing key or access — treat as off
        }

        return 0;
    }
}
