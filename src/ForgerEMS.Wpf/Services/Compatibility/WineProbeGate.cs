using System;
using System.Threading;
using VentoyToolkitSetup.Wpf.Infrastructure;

namespace VentoyToolkitSetup.Wpf.Services.Compatibility;

/// <summary>
/// Central decision point for whether a Windows-only probe (WMI, native
/// sensors, driver enumeration, etc.) should run in the current environment.
/// Under Wine the answer is "no, report Unsupported"; on native Windows the
/// probe runs as normal. Probes call this instead of inventing their own
/// platform checks so the policy stays in one place.
/// </summary>
/// <remarks>
/// Important: a probe that is gated off by this class must NOT lower scan
/// confidence or surface as a failure. Use
/// <see cref="WineProbeOutcome.UnsupportedUnderWine"/> to flag the result as
/// "compatibility limited" rather than "broken".
///
/// Test isolation: <see cref="OverrideEnvironment"/> is backed by an
/// <see cref="AsyncLocal{T}"/> so a test setting an override in one logical
/// call context cannot leak into a parallel test running on a different
/// context. Prefer <see cref="PushOverride"/> for try/finally-free scopes.
/// </remarks>
public static class WineProbeGate
{
    private static readonly AsyncLocal<CompatibilityEnvironment?> _override = new();

    /// <summary>
    /// Overrides the ambient environment for testing. Backed by
    /// <see cref="AsyncLocal{T}"/> so two parallel tests cannot clobber
    /// each other and so an override never leaks across test boundaries.
    /// Production callers leave this null and pick up <c>App.CompatibilityEnvironment</c>.
    /// </summary>
    public static CompatibilityEnvironment? OverrideEnvironment
    {
        get => _override.Value;
        set => _override.Value = value;
    }

    /// <summary>
    /// Convenience scope: push an override, dispose to restore the prior
    /// value. Pairs well with <c>using</c> in test methods.
    /// </summary>
    public static IDisposable PushOverride(CompatibilityEnvironment? environment)
    {
        var prior = _override.Value;
        _override.Value = environment;
        return new OverrideScope(prior);
    }

    private static CompatibilityEnvironment? Current => OverrideEnvironment ?? App.CompatibilityEnvironment;

    /// <summary>
    /// True only when the current ambient environment was explicitly
    /// identified as Wine. Probes that need to skip Windows-only calls
    /// should consult this property — not <see cref="IsCompatibilityMode"/>
    /// — so they never gate on weaker signals.
    /// </summary>
    public static bool IsWine
    {
        get
        {
            var env = Current;
            return env is { IsWine: true } &&
                   env.Platform == RuntimePlatformKind.WindowsUnderWine;
        }
    }

    /// <summary>
    /// True if the host is in compatibility mode. Today this is
    /// semantically identical to <see cref="IsWine"/> — pure Linux hosts
    /// do not trip this flag because ForgerEMS does not ship a native
    /// Linux process.
    /// </summary>
    public static bool IsCompatibilityMode => IsWine;

    /// <summary>
    /// True if the probe should run normally. Equivalent to
    /// <c>!IsWine</c>; named affirmatively so the call site reads as
    /// "if the probe is allowed".
    /// </summary>
    public static bool IsWindowsOnlyProbeAllowed => !IsWine;

    /// <summary>
    /// Build the standard "compatibility limited" message for a probe.
    /// Uses neutral language ("limited", not "failed") so downstream
    /// scoring does not penalise the user's hardware.
    /// </summary>
    public static string DescribeUnsupported(string probeName)
    {
        return $"{probeName} is unsupported in Wine compatibility mode; this is a host limitation, not a hardware fault.";
    }

    private sealed class OverrideScope : IDisposable
    {
        private readonly CompatibilityEnvironment? _prior;
        private bool _disposed;

        public OverrideScope(CompatibilityEnvironment? prior)
        {
            _prior = prior;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _override.Value = _prior;
        }
    }
}
