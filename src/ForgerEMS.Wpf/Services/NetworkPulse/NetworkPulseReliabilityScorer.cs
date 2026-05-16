namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

/// <summary>
/// Conservative, explainable reliability tier (no fractional “scores”).
/// </summary>
public static class NetworkPulseReliabilityScorer
{
    public static NetworkPulseReliabilityTier Compute(
        bool reach,
        bool icmpOk,
        double? pingMs,
        double? jitterMs,
        double lossPercent,
        int recentReachFailures,
        int recentUnstableRawStreak)
    {
        if (!reach && !icmpOk && recentReachFailures >= 3)
        {
            return NetworkPulseReliabilityTier.Poor;
        }

        if (!reach && icmpOk)
        {
            return NetworkPulseReliabilityTier.Fair;
        }

        if (!icmpOk && reach)
        {
            return NetworkPulseReliabilityTier.Fair;
        }

        var score = 0;
        if (reach && icmpOk)
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

        if (lossPercent <= 0.5)
        {
            score += 2;
        }
        else if (lossPercent < 3)
        {
            score += 0;
        }
        else if (lossPercent < 8)
        {
            score -= 2;
        }
        else
        {
            score -= 4;
        }

        if (recentReachFailures >= 1)
        {
            score -= 1;
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
            NetworkPulseReliabilityTier.Excellent => "Low loss, calm latency pattern — looks very dependable right now.",
            NetworkPulseReliabilityTier.Good => "Healthy pattern for routine work; keep an eye on loss if it trends up.",
            NetworkPulseReliabilityTier.Fair => "Usable, but one or more signals (reach, loss, or jitter) are mixed — verify if you see issues in apps.",
            NetworkPulseReliabilityTier.Poor => "Several signals look stressed — consider checking Wi‑Fi, VPN, or upstream congestion.",
            _ => reach || icmpOk
                ? "Not enough consistent samples yet — Network Pulse stays conservative."
                : "Connectivity samples are inconclusive — Network Pulse stays conservative."
        };

    public static string TierLabel(NetworkPulseReliabilityTier tier) =>
        tier switch
        {
            NetworkPulseReliabilityTier.Excellent => "Excellent",
            NetworkPulseReliabilityTier.Good => "Good",
            NetworkPulseReliabilityTier.Fair => "Fair",
            NetworkPulseReliabilityTier.Poor => "Poor",
            _ => "Unknown"
        };
}
