using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Infrastructure;

/// <summary>Long-form copy for scrollable About / FAQ / Legal / Privacy panels.</summary>
public static class InfoDocumentTexts
{
    private const string DeepSensorShortDisclosure =
        "Hardware X-Ray uses local read-only hardware sensors. When Deep Sensor Mode is enabled, ForgerEMS may use the bundled LibreHardwareMonitor provider to improve sensor coverage for temperatures, clocks, load, fan RPM, and storage wear where supported. Sensor access is local; ForgerEMS does not control fans, voltages, clocks, overclocking, undervolting, BIOS, or firmware. Unavailable readings are coverage limits, not failures.";

    public static string BuildAbout(string appVersion, string displayVersion, string frontendVersion, string backendVersion)
    {
        return $"""
            ForgerEMS — Forger Engineering Maintenance Suite (Public Preview)
            v{appVersion} — {displayVersion}

            Built by Forger Digital Solutions.
            {BetaSupportInfo.CopyrightNotice}

            Support: {BetaSupportInfo.SupportEmail}
            {BetaSupportInfo.BetaIssueSupportLine}

            WHAT THIS BUILD IS
            ForgerEMS v1.2.0 Public Preview is a free, offline-first Windows technician toolkit for safer Ventoy-oriented USB maintenance media, toolkit health, local system scans, USB benchmarking on removable targets, and Kyra — a grounded assistant that prefers facts from your own scans.

            CORE AREAS (honest maturity)
            • USB Builder — Beta: removable targets only; blocks Windows/system/internal OS drives and unsafe partitions.
            • System Intelligence — Beta: Hardware X-Ray, health scoring, FlipValue, Best Use / Device Fit, and local reports under %LOCALAPPDATA%\ForgerEMS\.
            • Hardware X-Ray — Beta: local read-only providers show detected hardware and available sensor coverage. Deep Sensor Mode may use bundled LibreHardwareMonitor technology where packaged and enabled.
            • FlipValue — Beta: local/offline resale guidance unless a live provider and location are configured.
            • Best Use / Device Fit — Beta: practical device-fit guidance for repair, resale, development, gaming, school, and technician use.
            • Toolkit Manager — Beta: managed vs manual/info items; manual links are expected where redistribution is gated.
            • Diagnostics — Beta: checklist, logs, experimental WSL helpers where enabled.
            • Kyra — Preview: offline/local deterministic answers first; optional online providers only when configured (Kyra Advanced).
            • USB Intelligence / port mapping — Pro Preview: best-effort port topology on Windows; benchmark-driven hints when you measure.

            HARDWARE X-RAY / DEEP SENSOR MODE
            {DeepSensorShortDisclosure}
            Third-party notices: providers\sensors\THIRD-PARTY-NOTICES.txt and docs\THIRD-PARTY-SENSOR-NOTICES.md.

            PRIVACY / SAFETY (summary)
            Telemetry and crash reporting default to off unless you enable them via environment variables (see docs/ENVIRONMENT.md). Reports, logs, and sensor data stay local unless you choose to export or share them. Review exports before sending. {BetaSupportInfo.DoNotEmailSecretsWarning}

            KYRA (OPTIONAL ONLINE PROVIDERS)
            Offline Kyra needs no API keys. Optional providers (OpenAI-compatible, LM Studio, Ollama, Gemini/Anthropic paths where stubbed, custom base URL): see docs/KYRA_PROVIDER_ENVIRONMENT_SETUP.md and docs/ENVIRONMENT.md. Kyra Advanced shows status without revealing secrets.

            UPDATES
            GitHub Releases power the in-app update checker (stable / beta / RC / preview semantics depend on Settings and release tags). See docs/UPDATE-SYSTEM-v1.2.0.md.

            VERSIONS
            App / frontend metadata: {frontendVersion}
            Bundled backend / compatibility: {backendVersion}

            PUBLIC PREVIEW
            Prerelease software is provided “as-is”. Behavior may change between builds. Prefer the ZIP download from GitHub Releases; see FAQ. When reporting issues, include version and steps — never secrets in email.
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

            Does ForgerEMS require HWiNFO, LibreHardwareMonitor, CPU-Z, or vendor tools?
            No. ForgerEMS ships approved local providers with the app where legally allowed. Deep Sensor Mode uses bundled read-only providers and does not require separate user downloads.

            What is Deep Sensor Mode?
            Deep Sensor Mode is an optional local read-only sensor mode that may improve Hardware X-Ray sensor coverage for temperatures, clocks, load, fan RPM, and storage wear when supported.

            Does ForgerEMS control my fans, voltage, clocks, BIOS, or firmware?
            No. ForgerEMS only reads supported sensor data. It does not control fans, voltages, clocks, overclocking, undervolting, BIOS, or firmware.

            Why are some sensors missing?
            Some machines do not expose certain sensors through Windows, firmware, drivers, permissions, or available read-only providers. Unavailable readings are coverage limits, not failures.

            Does ForgerEMS upload my sensor data?
            No automatic upload. Sensor reports and logs are local unless you choose to copy, export, or share them.

            Can Deep Sensor Mode require administrator access?
            Some sensors may require admin access, vendor drivers, or firmware support, but normal scans should not require admin. ForgerEMS reports unavailable readings honestly.

            Is LibreHardwareMonitor included?
            Yes, where packaged, ForgerEMS includes LibreHardwareMonitorLib as a bundled local read-only sensor provider under MPL-2.0 with license and notice files included.

            Can I turn Deep Sensor Mode off?
            Yes. Deep Sensor Mode can be Off or Read-only local sensors. Environment variable/testing overrides may also be supported.

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

            LIBREHARDWAREMONITOR / SENSOR PROVIDERS
            ForgerEMS may include LibreHardwareMonitorLib as a bundled local read-only sensor provider for Hardware X-Ray when Deep Sensor Mode is enabled.
            License: MPL-2.0.
            License path: providers\sensors\LICENSES\LibreHardwareMonitor-MPL-2.0.txt
            Third-party notice path: providers\sensors\THIRD-PARTY-NOTICES.txt
            ForgerEMS proprietary code remains separate from MPL-covered LibreHardwareMonitor code. If ForgerEMS modifies MPL-covered LibreHardwareMonitor files and distributes them, those modified files must be made available as required by MPL-2.0.
            ForgerEMS does not redistribute HWiNFO, AIDA64, CPU-Z, or other proprietary sensor tools unless a license explicitly allows it.
            Sensor providers are read-only. ForgerEMS does not control fans, voltage, clocks, overclocking, undervolting, BIOS, or firmware. Firmware/vendor/admin limitations may prevent some readings; unavailable readings are coverage limits, not failures.

            MANUAL DOWNLOADS
            Some tools require manual steps because of licensing, redistribution limits, commercial rules, or operator safety.

            ACCEPTABLE USE
            ForgerEMS must not be used for unauthorized access, password bypass on devices you do not own, malware, credential theft, piracy, or other illegal activity.

            YOUR RESPONSIBILITY
            You are responsible for complying with software licenses and local laws, and for backing up data before destructive steps.

            SYSTEM INTELLIGENCE
            Diagnostics, Hardware X-Ray, sensor coverage, and resale guidance are informative and may not be perfectly accurate. Confirm critical decisions with additional testing. There is no warranty that every sensor is exposed on every machine.

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

            SYSTEM INTELLIGENCE / HARDWARE X-RAY / DEEP SENSOR MODE
            ForgerEMS runs diagnostics locally. Deep Sensor Mode reads supported local hardware sensor data only while the app is running or System Intelligence / Hardware X-Ray scans are executed. No sensor data is sold, automatically uploaded, or automatically sent to support.
            Reports may include hardware model, CPU/GPU/RAM/storage info, battery info, network adapter details, USB device details, Windows version, provider status, and diagnostic notes.
            Default support reports should be redacted where supported, but you should review reports before sharing. Do not send product keys, serial numbers, service tags, API keys, tokens, passwords, private documents, or sensitive personal files to support.

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
