namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

/// <summary>
/// Conservative, explainable reliability tier (no fractional “scores”).
/// </summary>
public static class NetworkPulseReliabilityScorer
{
    // Human-priority reliability weighting (see PART 1 of the UX hardening pass):
    //   VERY HIGH: low loss, sustained reach, gateway/route healthy
    //   HIGH:      ICMP success + calm latency, DNS responsive
    //   MEDIUM:    HTTPS verification probe
    //   LOW:       captive-portal probe specifics
    //   VERY LOW:  a single transient failure cycle
    // A failed HTTPS probe with healthy other signals is therefore not enough to drop us to Fair;
    // sustained failure (>= 3 recent reach failures) is required.
    public static NetworkPulseReliabilityTier Compute(
        bool reach,
        bool icmpOk,
        double? pingMs,
        double? jitterMs,
        double lossPercent,
        int recentReachFailures,
        int recentUnstableRawStreak)
    {
        // Hard, sustained failure: no reach AND no ICMP across multiple cycles. The previous
        // code returned Fair the moment reach disagreed with ICMP — that's the bug we're fixing.
        if (!reach && !icmpOk && recentReachFailures >= 3)
        {
            return NetworkPulseReliabilityTier.Poor;
        }

        var score = 0;

        // VERY HIGH weight: packet loss is the single most reliable usability signal.
        if (lossPercent <= 0.5)
        {
            score += 3;
        }
        else if (lossPercent < 3)
        {
            score += 1;
        }
        else if (lossPercent < 8)
        {
            score -= 2;
        }
        else
        {
            score -= 4;
        }

        // HIGH weight: ICMP success + a calm ping pattern.
        if (icmpOk)
        {
            score += 2;
        }

        if (pingMs is > 0 and < 40)
        {
            score += 2;
        }
        else if (pingMs is > 0 and < 90)
        {
            score += 1;
        }
        else if (pingMs is >= 200)
        {
            score -= 2;
        }

        if (jitterMs is > 0 and < 12)
        {
            score += 1;
        }
        else if (jitterMs is >= 45)
        {
            score -= 2;
        }

        // MEDIUM weight: HTTPS verification probe. Worth something, but cannot by itself flip
        // a healthy network to Fair when ICMP/loss/jitter are all good.
        if (reach)
        {
            score += 1;
        }
        else if (icmpOk)
        {
            // HTTPS failed but ICMP succeeded — likely a verification mismatch (VPN, custom
            // DNS, content filter). Penalise only mildly, and only if it has happened more
            // than once recently.
            if (recentReachFailures >= 2)
            {
                score -= 1;
            }
        }
        else
        {
            // No ICMP and no HTTPS this cycle — soft penalty until streak escalates.
            score -= 1;
        }

        if (recentReachFailures >= 3)
        {
            score -= 2;
        }

        if (recentUnstableRawStreak >= 2)
        {
            score -= 1;
        }

        return score switch
        {
            >= 6 => NetworkPulseReliabilityTier.Excellent,
            >= 4 => NetworkPulseReliabilityTier.Good,
            >= 2 => NetworkPulseReliabilityTier.Fair,
            >= 0 => NetworkPulseReliabilityTier.Fair,
            _ => NetworkPulseReliabilityTier.Poor
        };
    }

    public static string Explain(NetworkPulseReliabilityTier tier, bool reach, bool icmpOk, double lossPercent) =>
        tier switch
        {
            NetworkPulseReliabilityTier.Excellent => "Low loss and calm latency — connection looks very dependable right now.",
            NetworkPulseReliabilityTier.Good => "Healthy and consistent for everyday work.",
            NetworkPulseReliabilityTier.Fair => reach
                ? "Usable with minor inconsistencies — check again if apps feel sluggish."
                : "Usable; one or more verification checks didn't match this cycle (often a VPN or custom-DNS quirk).",
            NetworkPulseReliabilityTier.Poor => "Several signals look stressed — consider checking Wi‑Fi, VPN, or upstream congestion.",
            _ => reach || icmpOk
                ? "Not enough consistent samples yet — Network Pulse stays conservative."
                : "Connectivity samples are inconclusive — Network Pulse stays conservative."
        };

    // Tier labels are shown in the popup. "Fair" was reading as "my internet is bad" even when
    // throughput and latency were healthy, so we surface a calmer label. Enum identity is
    // preserved for stability and tests.
    public static string TierLabel(NetworkPulseReliabilityTier tier) =>
        tier switch
        {
            NetworkPulseReliabilityTier.Excellent => "Excellent",
            NetworkPulseReliabilityTier.Good => "Good",
            NetworkPulseReliabilityTier.Fair => "Usable",
            NetworkPulseReliabilityTier.Poor => "Degraded",
            _ => "Unknown"
        };
}
