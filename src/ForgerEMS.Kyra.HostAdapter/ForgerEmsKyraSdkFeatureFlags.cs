namespace ForgerEMS.Kyra.HostAdapter;

/// <summary>Kyra.Sdk integration gate (disabled by default; not consumed by shipping Kyra tab).</summary>
public static class ForgerEmsKyraSdkFeatureFlags
{
    public const string EnabledEnvironmentVariable = "FORGEREMS_KYRA_SDK_ENABLED";

    /// <summary>Compile-time default; env may opt in during Phase 6c dogfood only.</summary>
    public const bool DefaultEnabled = false;

    /// <summary>Planned settings label (docs/tests until UI wiring).</summary>
    public const string DisabledUiLabel =
        "Kyra SDK integration (planned — not enabled in this build)";

    public static bool IsSdkEnabledFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable(EnabledEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return bool.TryParse(raw, out var enabled) && enabled;
    }

    /// <summary>True only when <see cref="EnabledEnvironmentVariable"/> is exactly "true" (case-insensitive).</summary>
    public static bool IsActive => IsSdkEnabledFromEnvironment();
}
