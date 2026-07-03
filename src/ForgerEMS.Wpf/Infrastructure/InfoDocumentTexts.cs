using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Infrastructure;

/// <summary>Long-form copy for scrollable About / FAQ / Legal / Privacy panels.</summary>
public static class InfoDocumentTexts
{
    private const string DeepSensorShortDisclosure =
        "Hardware X-Ray uses local read-only hardware sensors. When Deep Sensor Mode is enabled, ForgerEMS may use the bundled LibreHardwareMonitor provider to improve sensor coverage for temperatures, clocks, load, fan RPM, and storage wear where supported. Sensor access is local; ForgerEMS does not control fans, voltages, clocks, overclocking, undervolting, BIOS, firmware, or hardware writes. Deep Sensor Mode is not permanent admin permission; Windows may ask for UAC approval when you run Admin Inventory Scan. Unavailable readings are coverage limits, not failures.";

    public static string BuildDrForgeRoadmap()
    {
        return $"""
            Dr. Forge Advanced Sensors — CLI bridge and roadmap

            STATUS: Packaged CLI bridge available when configured. Advanced sensor depth remains roadmap.

            WHAT DR. FORGE IS IN THIS BUILD
            Dr. Forge runs as a separate local user-mode hardware intake/report tool. ForgerEMS can point at a packaged drforge.exe, verify its release manifest/checksums when present, run readiness checks, and generate local JSON reports/archives through the CLI process boundary.

            WHY IT IS A SEPARATE APP
            ForgerEMS is a stable Windows inventory + USB maintenance toolkit. Hardware intake belongs in a focused, read-only companion so ForgerEMS stays usable even if a provider stalls, throws, or cannot read a particular board. Dr. Forge runs in its own process, ships on its own cadence, and never blocks ForgerEMS.

            FIRST-VERSION SCOPE (READ-ONLY)
            • Windows APIs, WMI / CIM where useful, and performance counters.
            • Storage / SMART APIs where the OS exposes them.
            • Battery and power APIs (designed capacity, cycle count, charge rate where exposed).
            • Local report/archive generation through the packaged CLI/Core contract.
            • No fan control, voltage control, charging control, overclocking, firmware flashing, service install/start, auto-elevation, network calls, telemetry, or kernel driver hacks in the first bridge.

            FUTURE OPTIONAL DEPTH
            A signed local helper service or driver only after threat model, legal review, signing/revocation plan, crash containment, rollback strategy, and a beta hardware validation pass — not before.

            HANDOFF TO FORGEREMS
            ForgerEMS consumes the Dr. Forge packaged CLI/Core contract only. It does not load Dr. Forge WPF or provider internals. Missing packages show a setup-needed state. Reports stay local until you choose to copy, open, export, or include them in a support bundle.

            HONEST TRUTHFULNESS
            ForgerEMS will not fake CPU/GPU temperature, fan RPM, voltage, wattage, amperage, charge speed, or unsupported telemetry — not now, not in Dr. Forge, ever. Unavailable means the OS / driver / firmware did not expose it on this hardware. ForgerEMS does not claim full HWiNFO / CPU-Z / LibreHardwareMonitor parity.

            ROADMAP STATUS
            The in-app "Download Dr. Forge" button remains intentionally absent. Configure a packaged CLI path only when you already have a trusted Dr. Forge CLI package. Deep telemetry such as fan RPM, voltage rails, EC/SuperIO/MSR readings remains unavailable until future safe providers or signed privileged components exist.
            """;
    }

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
            ForgerEMS v1.2.3 Public Preview is a Windows technician utility suite for local-device support workflows, safer Ventoy-oriented USB maintenance media, Driver Hub/vendor guidance, USB/port mapping, drive validation, battery health and system specs summaries, safe removable-target benchmarks, and Kyra — a local-first assistant that prefers facts from your own scans.

            CORE AREAS (honest maturity)
            • USB Builder — Beta: removable targets only; blocks Windows/system/internal OS drives and unsafe partitions.
            • Local Device Snapshot / Hardware X-Ray — Beta: local read-only providers show detected hardware and available sensor coverage. Deep Sensor Mode may use bundled LibreHardwareMonitor technology where packaged and enabled.
            • Driver Hub — Preview: vendor-first links and guidance; no automatic driver install, service-tag upload, or firmware flashing.
            • FlipValue — Beta: local/offline resale guidance unless a live provider and location are configured.
            • Best Use / Device Fit — Beta: practical device-fit guidance for repair, resale, development, gaming, school, and technician use.
            • Toolkit Manager — Beta: managed vs manual/info items; manual links are expected where redistribution is gated.
            • Support / safety tools — Beta: persistent Live Logs panel and exportable redacted support bundles.
            • Kyra — Preview: offline/local deterministic answers first; optional online providers only when configured (Kyra Advanced).
            • Kyra Intelligence Network — Preview foundation: local-first repair memory plus optional anonymous community learning controls. Default is Local Only; community upload is off/disabled in this phase.
            • USB Intelligence / port mapping — Preview (all features available during beta): best-effort port topology on Windows; benchmark-driven hints when you measure.

            HARDWARE X-RAY / DEEP SENSOR MODE
            {DeepSensorShortDisclosure}
            Third-party notices: providers\sensors\THIRD-PARTY-NOTICES.txt and docs\THIRD-PARTY-SENSOR-NOTICES.md.

            PRIVACY / SAFETY (summary)
            Telemetry and crash reporting default to off unless you enable them via environment variables (see docs/ENVIRONMENT.md). Reports, logs, support bundles, Kyra context, and sensor data stay local unless you choose to export or share them. Review exports before sending. {BetaSupportInfo.DoNotEmailSecretsWarning}

            INDEPENDENCE / TRADEMARKS
            ForgerEMS is independent and is not affiliated with, sponsored by, or endorsed by Microsoft, Linux distributions, hardware vendors, driver vendors, or third-party tools referenced in the app. Names are used only to identify compatibility, official resources, or supported technician workflows.

            KYRA (OPTIONAL ONLINE PROVIDERS)
            Offline Kyra needs no API keys. Public beta can use ForgerEMS Kyra Gateway with only FORGEREMS_KYRA_GATEWAY_URL + FORGEREMS_KYRA_GATEWAY_BETA_TOKEN. Provider API keys stay server-side. Optional BYOK providers (OpenAI-compatible, LM Studio, Ollama, Gemini, Anthropic, custom base URL): see docs/KYRA_PROVIDER_ENVIRONMENT_SETUP.md and docs/ENVIRONMENT.md. Kyra Advanced shows status without revealing secrets.

            KYRA INTELLIGENCE NETWORK
            Local Kyra Memory stores sanitized machine-scoped repair notes on this PC. Anonymous community intelligence sharing is off by default and requires explicit opt-in. Community sharing is not active in this release — the setting is visible for preview only. ForgerEMS does not sell user data. Local Kyra Memory stays on this PC unless you explicitly enable a future sharing option. Provider API keys are stored server-side and are not included in the desktop app.

            UPDATES
            GitHub Releases power the in-app update checker (stable / beta / RC / preview semantics depend on Settings and release tags). See docs/UPDATE_SYSTEM.md.

            VERSIONS
            App / frontend metadata: {frontendVersion}
            Bundled backend / compatibility: {backendVersion}

            PUBLIC PREVIEW
            Prerelease software is provided “as-is”. Behavior may change between builds. GitHub Releases provide both the installer and portable ZIP. When reporting issues, include version and steps — never secrets in email.
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

            What is Dr. Forge Intake?
            Dr. Forge Intake is a local bridge to a packaged Dr. Forge CLI. Select drforge.exe or place the package under an app-local tools folder, then use Check Package, Generate Report, or Generate Archive. ForgerEMS verifies the release manifest/checksums when available and runs only the packaged CLI process. If the package is missing, ForgerEMS shows a setup-needed state instead of crashing.

            Does Dr. Forge claim full hardware-monitor parity?
            No. Dr. Forge reports what the local user-mode intake can read. Unavailable readings stay Unavailable, not zero. Deep telemetry such as fan RPM, voltage rails, EC/SuperIO/MSR readings remains unavailable until future safe providers or signed privileged components exist.

            Does ForgerEMS require HWiNFO, LibreHardwareMonitor, CPU-Z, or vendor tools?
            No. ForgerEMS ships approved local providers with the app where legally allowed. Deep Sensor Mode uses bundled read-only providers and does not require separate user downloads.

            What is Deep Sensor Mode?
            Deep Sensor Mode is an optional local read-only sensor mode that may improve Hardware X-Ray sensor coverage for temperatures, clocks, load, fan RPM, and storage wear when supported. It is not permanent administrator permission.

            Does ForgerEMS control my fans, voltage, clocks, BIOS, or firmware?
            No. ForgerEMS only reads supported sensor data. It does not control fans, voltages, clocks, overclocking, undervolting, BIOS, or firmware.

            Why are some sensors missing?
            Some machines do not expose certain sensors through Windows, firmware, drivers, permissions, or available read-only providers. Unavailable readings are coverage limits, not failures.

            Does ForgerEMS upload my sensor data?
            No automatic upload. Sensor reports and logs are local unless you choose to copy, export, or share them.

            Can Deep Sensor Mode require administrator access?
            Some sensors may require admin access, vendor drivers, or firmware support, but Deep Sensor Mode itself does not grant admin rights. Windows may ask for UAC approval when you run Elevated Scan. ForgerEMS reports unavailable readings honestly.

            Is LibreHardwareMonitor included?
            Yes, where packaged, ForgerEMS includes LibreHardwareMonitorLib as a bundled local read-only sensor provider under MPL-2.0 with license and notice files included.

            Can I turn Deep Sensor Mode off?
            Deep Sensor Mode defaults to Off. The in-app Settings toggle was retired in v1.2.3-preview.1; the mode is only enabled through the optional installer checkbox or an environment variable/testing override, and older saved values are ignored safely when unsupported.

            What does Kyra see?
            Offline Kyra uses rules and optional local reports you already generated. With your permission, a sanitized summary (no product keys, raw serials, or full private paths in the safe summary path) can be included for online providers and gateway paths.

            What is the Toolkit Readiness Score?
            A 0–100 score for your current toolkit state on the selected USB target. Starts at 100 and is reduced by missing required items, checksum failures, managed updates available, Verify Links failures, USB target warnings, and Ventoy issues. Labels: Ready (85 or above), Mostly Ready (70–84), Needs Attention (45–69), Not Ready (below 45 or hard blockers present). Run Refresh Health to recalculate. The score also shows your top strengths, top blockers, and a next recommended action.

            What is a machine profile?
            A local file ForgerEMS saves under %LOCALAPPDATA%\ForgerEMS\Runtime\profiles\ to remember this machine's health score, toolkit readiness, USB benchmark results, and resale estimates between sessions. The profile uses a ForgerEMS-generated ID — not your hardware serial number. It is never uploaded automatically. You can export or delete it from Settings → Kyra Assistant → Export Memory / Delete Memory.

            What does Verify Links do?
            Verify Links checks whether toolkit source URLs are reachable using safe HTTP HEAD requests, with a small ranged GET fallback for hosts that block HEAD. It records reachability, HTTP status, redirect hosts, and content-length hints — without downloading full installers or ISOs and without executing anything. Runs are timeout-bounded and cancellable. Offline gracefully shows Unknown / Offline rather than a false result. Broken links reduce the Readiness Score; verified links add confidence.

            What is Kyra Intelligence Network?
            Local-first repair memory + optional anonymous community learning. Default is Local Only. Local Kyra Memory can store sanitized machine-scoped repair notes on this PC; community sharing is off by default. Community sharing is not active in this release — the setting is visible for preview only. Use Settings → Kyra Assistant to export or delete Kyra memory. ForgerEMS does not sell user data. Local Kyra Memory stays on this PC unless you explicitly enable a future sharing option. Provider API keys are stored server-side and are not included in the desktop app.

            What is Kyra Research Mode?
            Current/live prompts such as crypto, stocks, weather, news, latest versions, drivers, Ventoy/tool releases, resale comps, current Windows issues, and CVEs route to configured live tools/providers first. If unavailable, Kyra should say the live tool/provider is unavailable or not configured and should not invent current data.

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
            ForgerEMS is independent and is not affiliated with, sponsored by, or endorsed by Microsoft, Linux distributions, hardware vendors, driver vendors, or third-party tools referenced in the app. Names are used only to identify compatibility, official resources, or supported technician workflows. Third-party logos, icons, and product marks must not be used as decorative ForgerEMS artwork unless explicit written permission or license proof is kept in the repo.

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

            TECHNICIAN-ASSIST SCOPE
            ForgerEMS is a Public Preview technician-assist tool. It is not a replacement for professional judgement and does not promise guaranteed repair, guaranteed data recovery, guaranteed malware removal, guaranteed hardware diagnosis, guaranteed driver/component compatibility, guaranteed pricing or marketplace accuracy, or guaranteed legal/regulatory compliance. Technicians remain responsible for confirming official vendor links, licensing, and safe use before they act on any output from this app.

            SYSTEM INTELLIGENCE
            Diagnostics, Hardware X-Ray, sensor coverage, and resale guidance are informative and may not be perfectly accurate. System Intelligence and Hardware X-Ray may report Unknown / NotExposed / Inferred when firmware, drivers, permissions, or sensor providers do not expose data; missing readings are coverage limits, not failures. Confirm critical decisions with additional testing. There is no warranty that every sensor is exposed on every machine.

            SUPPORT BUNDLES
            Support bundles are user-controlled. ForgerEMS attempts to redact local usernames, private paths, API keys, tokens, bearer values, and product keys from logs, diagnostics, and exported bundles, but you should still review every bundle before sharing it. ForgerEMS does not automatically upload support bundles, sensor data, scan reports, or USB inventories anywhere.

            MARKETPLACE / VALUE
            Estimates and listing-style guidance are estimates, not guarantees of sale price or outcome.

            REAL-TIME / API CONTENT
            When Kyra uses API-backed features, treat responses as informational unless you verify at the source.

            KYRA INTELLIGENCE
            Kyra Intelligence Network is Local-first repair memory + optional anonymous community learning. Local repair memory is sanitized and machine-scoped. Anonymous community learning is off by default and requires opt-in. Community sharing is not active in this release — the setting is visible for preview only. Users can keep Kyra local-only, export Kyra memory, or delete Kyra memory in Settings. ForgerEMS does not sell user data. Local Kyra Memory stays on this PC unless the user explicitly enables a future sharing option. Provider API keys are stored server-side and are not included in the desktop app.

            SUPPORT
            Do not email API keys, passwords, serial numbers, product keys, private documents, recovery keys, or sensitive personal data to support.
            """;
    }

    public static string BuildTermsOfService()
    {
        return $"""
            {BetaSupportInfo.CopyrightNotice}
            Support: {BetaSupportInfo.SupportEmail}
            {BetaSupportInfo.DoNotEmailSecretsWarning}

            TERMS VERSION
            {TermsConsentStore.CurrentTermsVersion}

            NOT LEGAL ADVICE
            These are project-provided software terms for this preview build. They are plain-English software-use terms, not legal advice, and no attorney review is claimed.

            WHAT FORGEREMS IS
            ForgerEMS is a Windows technician utility/toolkit: USB build maintenance, portable technician USB profiles, Driver Hub/vendor guidance, local device summaries (battery health and system specs), safe removable-target benchmark workflows, drive validation, port/USB mapping, and Kyra assistance. It is a tool that helps you work; it does not act on your behalf without your input, and it does not replace professional judgement.

            WHAT FORGEREMS IS NOT
            ForgerEMS is not a finished enterprise product, a magic automatic fixer, a complete replacement for OEM tools, a full live sensor suite, legal/warranty/compliance advice, or a substitute for certified repair services.

            YOUR RESPONSIBILITY
            You are responsible for reviewing and verifying any change, repair step, USB build, driver action, or recommendation before acting on it. Always confirm the correct target device, back up data before destructive operations, and use your own judgement alongside ForgerEMS output.

            NO GUARANTEES
            ForgerEMS does not guarantee diagnostics accuracy, repair results, data recovery, driver safety/compatibility, or the accuracy of Kyra (AI) responses. Prerelease software is provided "as-is", without warranties express or implied. Use at your own risk.

            DRIVER / USB / VENDOR TERMS
            Driver Hub and USB Builder links are informational and vendor-first. Some media, tools, drivers, firmware, and utilities require internet access, manual downloads, user-supplied files, official vendor accounts, system permissions, separate licenses, or acceptance of third-party terms. You are responsible for those licenses and terms.

            LOGS, SUPPORT BUNDLES, AND KYRA CONTEXT
            General Terms acceptance does not authorize sharing logs or context. ForgerEMS asks again before exporting support bundles or Kyra memory. Review exported files before sending them.

            ACCEPTABLE USE
            ForgerEMS must not be used for unauthorized access, password bypass on devices you do not own, malware, credential theft, piracy, or other illegal activity.

            CHANGES
            These terms may change between builds. If the terms version changes, ForgerEMS asks for acceptance again before unlocking the main tools. See Legal and Privacy in Settings for related disclosures.
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
            ForgerEMS stores reports, profiles, consent records, and logs under %LOCALAPPDATA%\ForgerEMS\ (including Runtime reports and logs). Nothing is uploaded automatically to Forger Digital Solutions when you run scans locally.

            SYSTEM INTELLIGENCE / HARDWARE X-RAY / DEEP SENSOR MODE
            ForgerEMS runs diagnostics locally. Deep Sensor Mode reads supported local hardware sensor data only while the app is running or System Intelligence / Hardware X-Ray scans are executed. No sensor data is sold, automatically uploaded, or automatically sent to support.
            Reports may include hardware model, CPU/GPU/RAM/storage info, battery info, network adapter details, USB device details, Windows version, provider status, and diagnostic notes.
            Default support reports should be redacted where supported, but you should review reports before sharing. Do not send product keys, serial numbers, service tags, API keys, tokens, passwords, private documents, or sensitive personal files to support.

            DR. FORGE INTAKE
            The Dr. Forge CLI bridge stores only the selected drforge.exe path, the last readiness state, and last local report/archive paths under ForgerEMS Runtime config. Generated Dr. Forge reports and archives stay under the local Runtime reports folder. They may include local device/context information, sensor availability, findings, notes, and unavailable telemetry reasons. Review reports before sharing. ForgerEMS includes Dr. Forge report/archive files in support bundles only when you generated them from the app or explicitly choose to include them.

            KYRA AND SANITIZED SUMMARIES
            Kyra uses sanitized summaries for external/provider paths where implemented. Do not paste API keys, passwords, serial numbers, product keys, private documents, or sensitive files into chat or support email.

            KYRA INTELLIGENCE NETWORK
            Default is Local Only. Local Kyra Memory can store sanitized machine-scoped repair notes on this PC, such as machine class, hardware category summary, health score band, issue/warning category, USB target safety result, best-use category, resale prep category, scan timestamp, and confidence. Anonymous community intelligence sharing is off by default and requires explicit opt-in. Community sharing is not active in this release — the setting is visible for preview only.
            ForgerEMS does not sell user data. Local Kyra Memory stays on this PC unless the user explicitly enables a future sharing option. Realtime Kyra Gateway sends only sanitized request context needed to answer current-data questions. Provider API keys are stored server-side and are not included in the desktop app. Anonymous Community Intelligence sharing is optional and off by default.

            LOGS AND REPORTS
            Technical diagnostics may appear in local logs. Anything you copy/export for sharing, including support bundles, Kyra context, reports, or safe summaries, should be reviewed first. Full logs may contain paths or device detail — redact before sending.

            ONLINE AI PROVIDERS
            If you configure an online provider in Kyra Advanced, prompts and optional context are sent according to that provider’s settings and your toggles. Offline/local modes remain available where implemented.

            REALTIME KYRA GATEWAY
            When enabled, current-data questions are answered via the ForgerEMS Worker using a sanitized prompt and optional broad System Intelligence summary. Provider API keys are not included in the desktop app; they live as Worker secrets. Disable with FORGEREMS_KYRA_GATEWAY_ENABLED=false or by clearing the gateway URL in Kyra Advanced.

            THIRD PARTIES
            Third-party tools you install or download separately are governed by their own privacy policies and terms.

            BETA
            Privacy-related behavior may evolve between beta builds; prefer the in-app Kyra Advanced panel for the current provider and sharing state.
            """;
    }

    public static string BuildThirdPartyNotices()
    {
        return $"""
            {BetaSupportInfo.CopyrightNotice}

            THIRD-PARTY NOTICES
            ForgerEMS references vendor names, operating systems, hardware brands, utilities, and download portals only to identify official resources or technician workflows. Those names remain the property of their owners.

            BUNDLED SENSOR PROVIDER
            Where packaged, ForgerEMS includes LibreHardwareMonitorLib as a local read-only sensor provider for Hardware X-Ray / Deep Sensor Mode.
            License: MPL-2.0.
            Notice path: providers\sensors\THIRD-PARTY-NOTICES.txt.
            License path: providers\sensors\LICENSES\LibreHardwareMonitor-MPL-2.0.txt.

            MANAGED DOWNLOADS AND USB BUILDER
            USB Builder may create vendor links, managed-download shortcuts, manual folders, and user-supplied file drop zones. Downloaded media and third-party tools are governed by their own licenses, vendor terms, redistribution rules, and privacy policies.

            INDEPENDENCE
            ForgerEMS is independent and is not affiliated with, sponsored by, or endorsed by Microsoft, Linux distributions, OEMs, driver vendors, hardware vendors, Ventoy, or third-party utilities referenced in the app unless a separate written agreement says otherwise.
            """;
    }
}
