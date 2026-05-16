using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking.Connectivity;

namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

public static class NetworkPulseWifiEnricher
{
    /// <summary>
    /// Contextual Wi‑Fi hints (not internet throughput). SSID is never included.
    /// </summary>
    public static Task<string?> TryGetContextLineAsync(NetworkPulseConnectionKind kind, CancellationToken cancellationToken)
    {
        if (kind != NetworkPulseConnectionKind.WiFi)
        {
            return Task.FromResult<string?>(null);
        }

        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is not { IsWlanConnectionProfile: true })
            {
                return Task.FromResult<string?>(null);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var parts = new System.Collections.Generic.List<string> { "Wi‑Fi" };

            try
            {
                var bars = profile.GetSignalBars();
                if (bars is >= 1 and <= 5)
                {
                    var approxPct = bars switch
                    {
                        5 => 90,
                        4 => 75,
                        3 => 55,
                        2 => 35,
                        _ => 15
                    };
                    parts.Add($"~{approxPct}% signal (approx., {bars}/5 bars)");
                }
            }
            catch
            {
            }

            return Task.FromResult<string?>(
                parts.Count <= 1 ? "Wi‑Fi (radio detail limited)" : string.Join(" • ", parts));
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }
}
