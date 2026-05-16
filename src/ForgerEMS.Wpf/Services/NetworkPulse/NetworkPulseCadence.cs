namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

internal static class NetworkPulseCadence
{
    public static int DefaultRefreshSeconds(NetworkPulseMode mode) =>
        mode switch
        {
            NetworkPulseMode.Light => 45,
            NetworkPulseMode.Balanced => 30,
            NetworkPulseMode.Full => 15,
            _ => 30
        };

    public static int DefaultProbeCadenceSeconds(NetworkPulseMode mode) =>
        mode switch
        {
            NetworkPulseMode.Light => 600,
            NetworkPulseMode.Balanced => 300,
            NetworkPulseMode.Full => 180,
            _ => 300
        };

    public static int ProbeMaxBytes(NetworkPulseMode mode) =>
        mode switch
        {
            NetworkPulseMode.Light => 131_072,
            NetworkPulseMode.Balanced => 262_144,
            NetworkPulseMode.Full => 524_288,
            _ => 262_144
        };

    public static int LatencySampleCount(NetworkPulseMode mode) =>
        mode switch
        {
            NetworkPulseMode.Light => 3,
            NetworkPulseMode.Balanced => 4,
            NetworkPulseMode.Full => 6,
            _ => 4
        };
}
