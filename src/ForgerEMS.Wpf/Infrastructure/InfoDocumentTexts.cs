using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Infrastructure;

/// <summary>Long-form copy for scrollable About / FAQ / Legal panels.</summary>
public static class InfoDocumentTexts
{
    public static string BuildAbout(string appVersion, string displayVersion, string frontendVersion, string backendVersion)
    {
        return $"""
            ForgerEMS (Beta) — v{appVersion} ({displayVersion})

            {BetaSupportInfo.CopyrightNotice}

            Support: {BetaSupportInfo.SupportEmail}
            {BetaSupportInfo.BetaIssueSupportLine}

            WHAT FORGEREMS IS
            ForgerEMS is a Windows technician toolkit for building and maintaining a Ventoy-based repair USB. It wraps a PowerShell backend with a native UI so you can verify bundles, prepare drives, refresh toolkits, and run diagnostics with clear confirmations for destructive steps.

            CORE AREAS
            • USB Builder — select targets, benchmark, Ventoy prep, and related workflows.
            • System Intelligence — local hardware health, storage, battery, security, and resale-oriented summaries.
            • Toolkit Manager — manifest-driven health for tools on the USB (installed, manual, placeholders, updates).
            • Diagnostics — logs, WSL helpers, link/file safety checks, and technician utilities.
            • Kyra — API-first assistant with local fallback; optional sanitized system context when you allow it.

            WHAT IT HELPS WITH
            Repair prep, diagnostics, resale/flip prep, OS choice, toolkit upkeep, and day-to-day technician workflows — without sending data off-device unless you opt in to online providers and context sharing.

            VERSIONS
            App / frontend metadata: {frontendVersion}
            Bundled backend / compatibility: {backendVersion}

            BETA
            Behavior and defaults may change between builds. Include version, steps, and screenshots when you contact support. {BetaSupportInfo.DoNotEmailSecretsWarning}
            """;
    }

    public static string BuildFaq()
    {
        return $"""
            {BetaSupportInfo.CopyrightNotice}
            {BetaSupportInfo.BetaIssueSupportLine}
            Support: {BetaSupportInfo.SupportEmail}
            {BetaSupportInfo.DoNotEmailSecretsWarning}

            What is ForgerEMS?
            A Windows desktop app that orchestrates the ForgerEMS backend: verify releases, prepare USB targets, refresh toolkits, read logs, run System Intelligence, use Diagnostics, and (optionally) Kyra — with strong offline paths.

            What kind of USB do I need?
            Recommend at least 64 GB for a comfortable repair kit; 128 GB is better if you want a fuller toolkit without juggling free space.

            Why are some tools manual downloads?
            Licensing, redistribution rules, vendor gating, or safety policies mean ForgerEMS cannot auto-fetch everything. Manual items open shortcuts or pages so you obtain files legally and deliberately.

            Why do some items show placeholders?
            Placeholders reserve space or document intent until you add the real payload (often after a manual download).

            Does ForgerEMS download everything automatically?
            No. Managed items follow the manifest; many items are manual by design. The app does not silently install third-party software without your action.

            Does ForgerEMS collect my API keys or passwords?
            No. API keys are not written into chat logs by design; session keys stay in memory unless you save provider config. Never send API keys, passwords, private files, or recovery keys in support email. {BetaSupportInfo.DoNotEmailSecretsWarning}

            What is Kyra?
            Kyra is the in-app assistant. She is API-first when providers are configured, uses sanitized System Intelligence context when you allow it, and falls back to local rules if online routes fail.

            What does System Intelligence scan?
            Local hardware and health-oriented signals: device identity, CPU/RAM/GPU, disks and health cues, battery, OS build, TPM/Secure Boot, network summary, and flip-oriented hints — used to answer “this PC” questions without you pasting specs.

            What does Toolkit Manager do?
            It compares the USB toolkit to the manifest: installed, missing required, updates, manual, placeholders, and failures, with next steps per item.

            What does Diagnostics do?
            Technician-oriented helpers: logs, safe command runners, link/file heuristics, WSL access, and similar tools — use only what you understand.

            What should beta testers report?
            Unexpected errors, confusing UI, backend detection issues, USB edge cases, and Kyra routing problems. Include version, steps, and screenshots. Use Copy logs / Runtime logs — not secrets.

            How do I send logs safely?
            Use Live Logs or View Full Logs, and the Runtime folder under %LOCALAPPDATA%\ForgerEMS\Runtime\logs. Redact paths if needed. Do not paste API keys or passwords into email.
            """;
    }

    public static string BuildLegal()
    {
        return $"""
            {BetaSupportInfo.CopyrightNotice}
            Beta feedback: {BetaSupportInfo.SupportEmail}
            {BetaSupportInfo.BetaIssueSupportLine}
            {BetaSupportInfo.DoNotEmailSecretsWarning}

            THIRD-PARTY TOOLS
            ForgerEMS is a repair/diagnostic toolkit. It does not own third-party tools, ISOs, or vendor software. Those belong to their respective owners and their licenses apply.

            MANUAL DOWNLOADS
            Some tools require manual download because of licensing, redistribution limits, commercial rules, or operator safety.

            ACCEPTABLE USE
            ForgerEMS must not be used for unauthorized access, password bypass on devices you do not own, malware, credential theft, piracy, or other illegal activity.

            YOUR RESPONSIBILITY
            You are responsible for complying with software licenses and local laws, and for backing up data before destructive steps.

            SYSTEM INTELLIGENCE
            Diagnostics and resale guidance are informative and may not be perfectly accurate. Confirm critical decisions with additional testing.

            MARKETPLACE / VALUE
            Estimates and listing-style guidance are estimates, not guarantees of sale price or outcome.

            REAL-TIME / API CONTENT
            When Kyra uses API-backed features (weather, markets, news, etc.), treat responses as informational only unless you verify at the source.

            SUPPORT
            Do not email API keys, passwords, serial numbers, private documents, recovery keys, or sensitive personal data to support.

            BETA SOFTWARE
            Prerelease software is provided as-is without warranty. Use at your own risk.
            """;
    }
}
