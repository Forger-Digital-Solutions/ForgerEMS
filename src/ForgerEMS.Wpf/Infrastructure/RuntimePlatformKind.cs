namespace VentoyToolkitSetup.Wpf.Infrastructure;

/// <summary>
/// Coarse classification of the host runtime, used by ForgerEMS to gate
/// platform-specific behavior (WPF render mode, Windows-only probes, etc.).
/// </summary>
public enum RuntimePlatformKind
{
    Unknown = 0,
    WindowsNative = 1,
    WindowsUnderWine = 2,
    LinuxHostLikely = 3
}
