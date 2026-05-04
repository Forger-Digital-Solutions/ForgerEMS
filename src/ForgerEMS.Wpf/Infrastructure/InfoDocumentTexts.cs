using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Infrastructure;

/// <summary>Long-form copy for scrollable About / FAQ / Legal / Privacy panels.</summary>
public static class InfoDocumentTexts
{
    public static string BuildAbout(string appVersion, string displayVersion, string frontendVersion, string backendVersion)
    {
        return $"""
            ForgerEMS — Forger Engineering Maintenance Suite (Beta)
            v{appVersion} ({displayVersion})

            Built by Forger Digital Solutions.
            {BetaSupportInfo.CopyrightNotice}

            Support: {BetaSupportInfo.SupportEmail}
            {BetaSupportInfo.BetaIssueSupportLine}

            PURPOSE
            USB toolkit builder, System Intelligence, Diagnostics, Toolkit Manager, and Kyra — a Windows desktop companion for technician workflows with a PowerShell backend and optional online AI when you configure it.

            CORE AREAS
            • USB Builder — select removable targets, safety checks, Ventoy-related workflows, and USB Intelligence (Pro preview).
            • System Intelligence — local hardware and health-oriented summaries (stored under %LOCALAPPDATA%\ForgerEMS\).
            • Toolkit Manager — manifest-driven health (Managed Ready, Managed Missing, Manual Required, and related states).
            • Diagnostics — unified checklist, logs, WSL helpers, and link/file safety tools.
            • Kyra — in-app assistant; offline rules by default, optional providers in Kyra Advanced.

            PRIVACY / SAFETY (summary)
            Reports and logs stay local unless you share them. Review exports before sending. {BetaSupportInfo.DoNotEmailSecretsWarning}

            KYRA (OPTIONAL ONLINE PROVIDERS)
            Offline / default Kyra needs no API keys. For optional cloud or local-server providers (Gemini, Groq, OpenAI-compatible, Anthropic path, LM Studio, Ollama, etc.), environment variable names, base URLs, and troubleshooting: see the repository file docs/KYRA_PROVIDER_ENVIRONMENT_SETUP.md. Use Kyra Advanced in the app to confirm which provider is active.

            VERSIONS
            App / frontend metadata: {frontendVersion}
            Bundled backend / compatibility: {backendVersion}

            BETA
            Prerelease software is provided “as-is”. Behavior may change between builds. Prefer the ZIP download from GitHub Releases for beta testing; see FAQ. When reporting issues, include version and steps — never secrets in email.
            """;
    }

    public static string BuildFaq()
    {
        return $"""
            {BetaSupportInfo.CopyrightNotice}
            {BetaSupportInfo.BetaIssueSupportLine}
            Support: {BetaSupportInfo.SupportEmail}
            {BetaSupportInfo.DoNotEmailSecretsWarning}

            Why does Windows or Microsoft Edge warn about the installer?
            SmartScreen and similar protections often flag new or unsigned installers. ForgerEMS does not bypass Windows security. The ZIP flow lets you inspect files and checksums before running anything; the raw installer is labeled for advanced/direct use.

            Why should beta testers download the ZIP?
            You get START_HERE.bat, VERIFY.txt, CHECKSUMS.sha256, and the installer in one bundle — easier to verify integrity and a gentler first-run path than launching an unfamiliar EXE directly.

            How do I verify CHECKSUMS.sha256?
            From PowerShell in the folder containing the ZIP and checksum file, compare the published hash to your file (for example with Get-FileHash on the ZIP and match the line in CHECKSUMS.sha256).

            Why does USB speed say “Not measured yet”?
            Speeds come from a USB benchmark after you select a safe removable target. Until then, the UI shows that no measurement has been recorded for the current selection.

            How do I use USB mapping?
            Select your USB, tap Start USB Mapping, use Capture Current Port, move the stick to another port, use Detect Port Change, enter a short label, then Save Port Label. Labels are stored in your local USB machine profile.

            Does ForgerEMS upload my system info?
            No automatic upload. System Intelligence and related reports are written under %LOCALAPPDATA%\ForgerEMS\. If you enable an online Kyra provider and allow context sharing, only the sanitized context described in Kyra Advanced is sent according to your settings.

            What does Kyra see?
            Offline Kyra uses rules and optional local reports you already generated. With your permission, a sanitized summary (no product keys, raw serials, or full private paths in the safe summary path) can be included for online providers.

            What is Free vs Pro preview?
            During beta, some “Pro” or preview capabilities may be visible for feedback; licensing is not enforced yet. Treat preview labels as informational, not a final entitlement.

            Why are some toolkit items Manual Required?
            Licensing, vendor gating, or verification limits mean ForgerEMS cannot legally or safely auto-download those payloads. Use the provided links or instructions, place files where the manifest expects, then refresh health.

            Where are logs stored?
            Typical locations include %LOCALAPPDATA%\ForgerEMS\logs and %LOCALAPPDATA%\ForgerEMS\Runtime\logs. Use View Full Logs in the app and review before sharing.

            How do I report beta issues?
            Email {BetaSupportInfo.SupportEmail} with version, Windows build, steps, expected vs actual, and screenshots. Attach sanitized log excerpts only — {BetaSupportInfo.DoNotEmailSecretsWarning}

            What is ForgerEMS in one line?
            Forger Engineering Maintenance Suite: USB toolkit maintenance, system intelligence, diagnostics, toolkit health, and Kyra — built by Forger Digital Solutions.

            What kind of USB do I need?
            Recommend at least 64 GB for a comfortable repair kit; 128 GB is better for a fuller toolkit.

            Does ForgerEMS download everything automatically?
            No. Managed items follow the manifest; many items are manual by design.

            What does Diagnostics do?
            Read-only style guidance and utilities: checklist, logs, WSL helpers, link/file heuristics — use only what you understand.
            """;
    }

    public static string BuildLegal()
    {
        return $"""
            {BetaSupportInfo.CopyrightNotice}
            Beta feedback: {BetaSupportInfo.SupportEmail}
            {BetaSupportInfo.BetaIssueSupportLine}
            {BetaSupportInfo.DoNotEmailSecretsWarning}

            NOT LEGAL ADVICE
            This text is practical software disclaimer language only; it is not legal advice.

            BETA / AS-IS
            Prerelease software is provided “as-is”, without warranties express or implied. Use at your own risk.

            USB AND STORAGE RISK
            You are responsible for selecting the correct USB, device, and partition. USB building and related operations can erase or modify drives depending on the operation. ForgerEMS attempts safety checks but cannot guarantee against user error, hardware faults, or OS quirks.

            PRO / LICENSING
            Pro or preview features may appear during beta for feedback; enforcement and commercial terms are not final unless separately published.

            THIRD-PARTY TOOLS
            ForgerEMS may reference, integrate with, download, or guide you to third-party tools (for example Ventoy or manifest-listed utilities). Those tools remain under their own licenses and terms. ForgerEMS does not claim ownership of them.

            MANUAL DOWNLOADS
            Some tools require manual steps because of licensing, redistribution limits, commercial rules, or operator safety.

            ACCEPTABLE USE
            ForgerEMS must not be used for unauthorized access, password bypass on devices you do not own, malware, credential theft, piracy, or other illegal activity.

            YOUR RESPONSIBILITY
            You are responsible for complying with software licenses and local laws, and for backing up data before destructive steps.

            SYSTEM INTELLIGENCE
            Diagnostics and resale guidance are informative and may not be perfectly accurate. Confirm critical decisions with additional testing.

            MARKETPLACE / VALUE
            Estimates and listing-style guidance are estimates, not guarantees of sale price or outcome.

            REAL-TIME / API CONTENT
            When Kyra uses API-backed features, treat responses as informational unless you verify at the source.

            SUPPORT
            Do not email API keys, passwords, serial numbers, product keys, private documents, recovery keys, or sensitive personal data to support.
            """;
    }

    public static string BuildPrivacy()
    {
        return $"""
            {BetaSupportInfo.CopyrightNotice}
            {BetaSupportInfo.BetaIssueSupportLine}
            Support: {BetaSupportInfo.SupportEmail}
            {BetaSupportInfo.DoNotEmailSecretsWarning}

            LOCAL STORAGE
            ForgerEMS stores reports, profiles, and logs under %LOCALAPPDATA%\ForgerEMS\ (including Runtime reports and logs). Nothing is uploaded automatically to Forger Digital Solutions when you run scans locally.

            KYRA AND SANITIZED SUMMARIES
            Kyra uses sanitized summaries for external/provider paths where implemented. Do not paste API keys, passwords, serial numbers, product keys, private documents, or sensitive files into chat or support email.

            LOGS AND REPORTS
            Technical diagnostics may appear in local logs. Anything you copy for sharing (for example “Copy Safe Summary”) should be reviewed first. Full logs may contain paths or device detail — redact before sending.

            ONLINE AI PROVIDERS
            If you configure an online provider in Kyra Advanced, prompts and optional context are sent according to that provider’s settings and your toggles. Offline/local modes remain available where implemented.

            THIRD PARTIES
            Third-party tools you install or download separately are governed by their own privacy policies and terms.

            BETA
            Privacy-related behavior may evolve between beta builds; prefer the in-app Kyra Advanced panel for the current provider and sharing state.
            """;
    }
}
