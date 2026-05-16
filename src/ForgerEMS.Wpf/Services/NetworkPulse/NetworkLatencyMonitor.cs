using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

public static class NetworkLatencyMonitor
{
    private const string IcmpTargetHost = "dns.google";

    public static async Task<LatencyMonitorResult> MeasureAsync(int icmpSamples, CancellationToken cancellationToken)
    {
        if (icmpSamples < 1)
        {
            icmpSamples = 1;
        }

        var ping = new Ping();
        try
        {
            double? gatewayMs = await TryPingFirstGatewayAsync(ping, cancellationToken).ConfigureAwait(false);
            double? dnsMs = await TryDnsTimingAsync(cancellationToken).ConfigureAwait(false);

            var rtts = new double[icmpSamples];
            var failures = 0;
            for (var i = 0; i < icmpSamples; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var reply = await ping.SendPingAsync(IcmpTargetHost, 1200).ConfigureAwait(false);
                    if (reply.Status == IPStatus.Success && reply.RoundtripTime >= 0)
                    {
                        rtts[i] = reply.RoundtripTime;
                    }
                    else
                    {
                        failures++;
                        rtts[i] = double.NaN;
                    }
                }
                catch
                {
                    failures++;
                    rtts[i] = double.NaN;
                }

                await Task.Delay(35, cancellationToken).ConfigureAwait(false);
            }

            var ok = rtts.Where(static v => !double.IsNaN(v)).ToArray();
            var loss = icmpSamples == 0 ? 0d : (failures * 100.0) / icmpSamples;
            double? median = ok.Length == 0 ? null : ok.OrderBy(v => v).ElementAt(ok.Length / 2);
            double? jitter = ComputeJitter(ok);

            return new LatencyMonitorResult(median, jitter, loss, gatewayMs, dnsMs, ok.Length > 0);
        }
        finally
        {
            ping.Dispose();
        }
    }

    private static async Task<double?> TryPingFirstGatewayAsync(Ping ping, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                var props = ni.GetIPProperties();
                foreach (var gw in props.GatewayAddresses)
                {
                    if (gw.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    if (IPAddress.IsLoopback(gw.Address))
                    {
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    var reply = await ping.SendPingAsync(gw.Address, 900).ConfigureAwait(false);
                    if (reply.Status == IPStatus.Success && reply.RoundtripTime >= 0)
                    {
                        return reply.RoundtripTime;
                    }

                    return null;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static async Task<double?> TryDnsTimingAsync(CancellationToken cancellationToken)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _ = await Dns.GetHostAddressesAsync("example.com", cancellationToken).ConfigureAwait(false);
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds;
        }
        catch
        {
            return null;
        }
    }

    private static double? ComputeJitter(double[] okRtts)
    {
        if (okRtts.Length < 2)
        {
            return null;
        }

        var sum = 0d;
        for (var i = 1; i < okRtts.Length; i++)
        {
            sum += Math.Abs(okRtts[i] - okRtts[i - 1]);
        }

        return sum / (okRtts.Length - 1);
    }
}

public readonly record struct LatencyMonitorResult(
    double? IcmpMedianMs,
    double? JitterMs,
    double PacketLossPercent,
    double? GatewayPingMs,
    double? DnsLookupMs,
    bool AnyIcmpSuccess);
