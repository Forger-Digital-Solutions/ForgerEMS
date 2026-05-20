using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

public readonly record struct NetworkAdapterSummary(
    string Name,
    NetworkPulseConnectionKind Kind,
    double? LinkSpeedMbpsEstimated,
    bool HasUsableRoute,
    bool IsVirtual,
    bool HasApipaOnly,
    string RouteDetail);

public static class NetworkPulseEnvironmentReader
{
    public static NetworkAdapterSummary TryGetActiveAdapter()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(static n => n.OperationalStatus == OperationalStatus.Up)
                         .Select(BuildCandidate)
                         .Where(static c => c.HasUsableRoute)
                         .OrderBy(static c => c.IsVirtual)
                         .ThenByDescending(static c => c.LinkSpeedMbpsEstimated ?? 0))
            {
                return ni;
            }
        }
        catch
        {
        }

        return new NetworkAdapterSummary(
            "No active default route",
            NetworkPulseConnectionKind.Unknown,
            null,
            false,
            false,
            false,
            "No usable IPv4 adapter with a non-APIPA address and default gateway was found.");
    }

    private static NetworkAdapterSummary BuildCandidate(NetworkInterface ni)
    {
        var props = ni.GetIPProperties();
        var ipv4 = props.UnicastAddresses
            .Where(static u => u.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(static u => u.Address)
            .ToArray();
        var hasNonApipaIpv4 = ipv4.Any(IsUsableIpv4Address);
        var hasApipaOnly = ipv4.Length > 0 && !hasNonApipaIpv4;
        var hasGateway = props.GatewayAddresses.Any(static g =>
            g.Address.AddressFamily == AddressFamily.InterNetwork &&
            !IPAddress.IsLoopback(g.Address) &&
            !g.Address.Equals(IPAddress.Any));
        var isVirtual = IsVirtualAdapter(ni.Name, ni.Description, ni.NetworkInterfaceType);
        var kind = ni.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Ethernet => NetworkPulseConnectionKind.Ethernet,
            NetworkInterfaceType.GigabitEthernet => NetworkPulseConnectionKind.Ethernet,
            NetworkInterfaceType.Wireless80211 => NetworkPulseConnectionKind.WiFi,
            _ => NetworkPulseConnectionKind.Other
        };

        if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
        {
            hasGateway = false;
            hasNonApipaIpv4 = false;
        }

        var route = hasNonApipaIpv4 && hasGateway;
        double? linkMbps = ni.Speed > 0 ? ni.Speed / 1_000_000.0 : null;
        var detail = route
            ? (isVirtual
                ? "Usable default route is on a virtual/VPN adapter."
                : "Usable default route found.")
            : hasApipaOnly
                ? "Adapter has only APIPA/link-local IPv4 addresses."
                : "No usable default route on this adapter.";

        return new NetworkAdapterSummary(ni.Name, kind, linkMbps, route, isVirtual, hasApipaOnly, detail);
    }

    internal static bool IsUsableIpv4Address(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork &&
               !IPAddress.IsLoopback(address) &&
               !address.Equals(IPAddress.Any) &&
               bytes is not [169, 254, _, _];
    }

    internal static bool IsVirtualAdapter(string name, string description, NetworkInterfaceType type)
    {
        if (type is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
        {
            return true;
        }

        var text = $"{name} {description}";
        return text.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("WSL", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("TAP-", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("VPN", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Bluetooth Device (Personal Area Network)", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetMeteredConnection(out bool metered)
    {
        metered = false;
        try
        {
            var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            if (profile is null)
            {
                return false;
            }

            var cost = profile.GetConnectionCost();
            metered = cost.NetworkCostType is Windows.Networking.Connectivity.NetworkCostType.Fixed
                or Windows.Networking.Connectivity.NetworkCostType.Variable;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetEnergySaverEnabled(out bool enabled)
    {
        enabled = false;
        try
        {
            enabled = Windows.System.Power.PowerManager.EnergySaverStatus == Windows.System.Power.EnergySaverStatus.On;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
