using System;
using System.Collections.Generic;
using System.Linq;

namespace ForgerEMS.Wpf.Services.WindowsMaintenance;

/// <summary>
/// Pure mapping from a <see cref="WindowsMaintenanceSnapshot"/> to read-only display lines.
/// Every line reflects the exact probe state — a WUA result code of 2 means the search
/// completed, not that no updates apply; a stopped manual service is informational, not
/// proof of a fault; "NoListedPolicyDetected"/"NoKnownPendingReboot" are never reworded
/// into their opposites.
/// </summary>
public static class WindowsMaintenanceDisplayMapper
{
    public static IReadOnlyList<string> BuildFindings(WindowsMaintenanceSnapshot snapshot)
    {
        var findings = new List<string>();

        var os = snapshot.OperatingSystem;
        if (os is not null)
        {
            findings.Add(
                $"OS support status: {os.SupportStatus}; servicing status: {os.ServicingStatus}.");
        }

        foreach (var service in snapshot.Services)
        {
            if (service.StartType.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(
                    $"Service {service.Name}: {service.Status}/Disabled — requires user action to enable.");
            }
            else if (service.Status.Equals("Stopped", StringComparison.OrdinalIgnoreCase)
                     || service.StartType.Equals("Manual", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(
                    $"Service {service.Name}: {service.Status}/{service.StartType} — " +
                    "informational only; a stopped or manual-start service is not proof of a fault.");
            }
        }

        var reboot = snapshot.Reboot;
        if (reboot.State.Equals("PendingReboot", StringComparison.OrdinalIgnoreCase)
            || reboot.Indicators.Count > 0)
        {
            var indicators = reboot.Indicators.Count > 0
                ? string.Join(", ", reboot.Indicators)
                : "state reported by probe";
            findings.Add($"Pending reboot detected ({reboot.State}): {indicators}.");
        }
        else if (reboot.State.Equals("NoKnownPendingReboot", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add("No known pending reboot.");
        }
        else
        {
            findings.Add($"Pending-reboot state could not be fully established ({reboot.State}).");
        }

        var policy = snapshot.Policy;
        var completeness = string.IsNullOrWhiteSpace(policy.Completeness)
            ? string.Empty
            : $" {policy.Completeness}";
        var listedPolicy = policy.Values.Count > 0
            || policy.State.Equals("ListedPolicyDetected", StringComparison.OrdinalIgnoreCase)
            || policy.State.Equals("Detected", StringComparison.OrdinalIgnoreCase);
        if (listedPolicy)
        {
            findings.Add(
                $"Windows Update policy is listed as configured ({policy.State}); " +
                "behavior may differ from defaults." + completeness);
            foreach (var value in policy.Values.Take(10))
            {
                findings.Add($"  Policy {value.Name} = {value.Value}");
            }
        }
        else if (policy.State.Equals("NoListedPolicyDetected", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add("No listed Windows Update policy detected." + completeness);
        }
        else
        {
            findings.Add($"Windows Update policy state: {policy.State}." + completeness);
        }

        AppendUpdateFindings(findings, snapshot.Updates);

        foreach (var error in snapshot.Errors)
        {
            findings.Add(
                $"Probe warning ({error.Stage ?? error.Category ?? "unknown"}): " +
                $"{error.Category} (0x{error.HResult:X8})");
        }

        return findings;
    }

    private static void AppendUpdateFindings(List<string> findings, WindowsUpdateInfo updates)
    {
        // ResultCode 2 means the WUA search completed successfully — it does NOT mean no
        // updates apply; real offers can accompany a successful search.
        if (updates.State.Equals("UpdateAvailable", StringComparison.OrdinalIgnoreCase)
            && updates.Items.Count > 0)
        {
            findings.Add(
                $"{updates.Items.Count} Windows update offer(s) reported by WUA " +
                "(offers are not installed and do not prove applicability).");
            foreach (var offer in updates.Items.Take(10))
            {
                var kind = offer.Kind ?? "Update";
                var note = offer.UserActionRequired == true ? " — requires user action" : string.Empty;
                findings.Add($"  {kind}: {offer.Title ?? offer.Id ?? "untitled"}{note}");
            }

            return;
        }

        if (updates.State.Equals("NoApplicableUpdatesOffered", StringComparison.OrdinalIgnoreCase)
            && updates.ResultCode == 2
            && updates.Items.Count == 0)
        {
            findings.Add(
                "WUA search completed successfully; no applicable updates were offered this time.");
            return;
        }

        if (updates.State.Equals("Cached", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(
                $"Windows Update result served from cache (checked {updates.CheckedAtUtc ?? "unknown"}).");
            return;
        }

        if (updates.State.Equals("NotChecked", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add("Windows Update check was not performed.");
            return;
        }

        if (updates.ResultCode is not null && updates.ResultCode != 2)
        {
            findings.Add(
                $"WUA search did not complete successfully (result code {updates.ResultCode})." +
                (string.IsNullOrWhiteSpace(updates.Reason) ? string.Empty : $" {updates.Reason}"));
            return;
        }

        findings.Add(
            $"Windows Update state: {updates.State}." +
            (string.IsNullOrWhiteSpace(updates.Reason) ? string.Empty : $" {updates.Reason}"));
    }

    public static string BuildStatusLine(WindowsMaintenanceSnapshot snapshot, bool failed, bool cached)
    {
        var os = snapshot.OperatingSystem;
        var versionLine = os is not null
            ? $"Windows {os.Version ?? os.Name ?? "unknown"} ({os.Architecture ?? "unknown arch"})"
            : "Windows maintenance snapshot";

        if (failed)
        {
            return $"Scan failed — showing last-good snapshot captured {snapshot.CapturedAtUtc}. {versionLine} — read-only diagnostics";
        }

        if (cached)
        {
            return $"Showing cached snapshot captured {snapshot.CapturedAtUtc}. {versionLine} — read-only diagnostics";
        }

        return $"{versionLine} — read-only diagnostics";
    }

    public static string BuildDetailLine(WindowsMaintenanceSnapshot snapshot) =>
        $"Captured {snapshot.CapturedAtUtc} via {snapshot.Source}. " +
        "All classifications are diagnostic only — no driver removal or update installation is permitted.";
}
