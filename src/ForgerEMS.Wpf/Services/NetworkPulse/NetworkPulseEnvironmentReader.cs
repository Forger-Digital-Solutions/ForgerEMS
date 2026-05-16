using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

public readonly record struct NetworkAdapterSummary(
    string Name,
    NetworkPulseConnectionKind Kind,
    double? LinkSpeedMbpsEstimated);

public static class NetworkPulseEnvironmentReader
{
    public static NetworkAdapterSummary TryGetActiveAdapter()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(static n => n.OperationalStatus == OperationalStatus.Up)
                         .OrderByDescending(static n => n.Speed))
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var props = ni.GetIPProperties();
                if (!props.UnicastAddresses.Any(static u => u.Address.AddressFamily == AddressFamily.InterNetwork))
                {
                    continue;
                }

                if (!props.GatewayAddresses.Any(static g => g.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(g.Address)))
                {
                    continue;
                }

                var kind = ni.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Ethernet => NetworkPulseConnectionKind.Ethernet,
                    NetworkInterfaceType.GigabitEthernet => NetworkPulseConnectionKind.Ethernet,
                    NetworkInterfaceType.Wireless80211 => NetworkPulseConnectionKind.WiFi,
                    _ => NetworkPulseConnectionKind.Other
                };

                double? linkMbps = null;
                if (ni.Speed > 0)
                {
                    linkMbps = ni.Speed / 1_000_000.0;
                }

                return new NetworkAdapterSummary(ni.Name, kind, linkMbps);
            }

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(static n => n.OperationalStatus == OperationalStatus.Up)
                         .OrderByDescending(static n => n.Speed))
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var kind = ni.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Ethernet => NetworkPulseConnectionKind.Ethernet,
                    NetworkInterfaceType.GigabitEthernet => NetworkPulseConnectionKind.Ethernet,
                    NetworkInterfaceType.Wireless80211 => NetworkPulseConnectionKind.WiFi,
                    _ => NetworkPulseConnectionKind.Other
                };

                double? linkMbps = ni.Speed > 0 ? ni.Speed / 1_000_000.0 : null;
                return new NetworkAdapterSummary(ni.Name, kind, linkMbps);
            }
        }
        catch
        {
        }

        return new NetworkAdapterSummary("Unknown", NetworkPulseConnectionKind.Unknown, null);
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
