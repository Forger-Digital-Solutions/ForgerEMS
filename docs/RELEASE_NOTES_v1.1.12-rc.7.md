# ForgerEMS v1.1.12-rc.7 — Kyra beta sign-off

## Kyra (beta-facing)

ForgerEMS includes **Kyra**, a unified on-device copilot for technicians. Kyra reads **local system and toolkit facts first** (System Intelligence, USB Builder context, toolkit health). **Online assist** can improve explanations when enabled, but it **cannot override** what the app already knows about this PC.

- **USB Builder safety**: Destructive actions stay on the correct removable targets. The Windows system volume is never offered as a Ventoy/USB imaging target, and Kyra will not help bypass those protections.
- **Honest live data**: Weather, news, market prices, and similar “right now” answers use **configured live tools** only. If a tool is not enabled, Kyra says that plainly instead of inventing numbers.
- **Conversation memory**: Optional **local, redacted** memory helps with follow-up questions (“fix those issues”, “that USB”, “explain simpler”) without sending your history to the cloud.
- **Privacy in the UI**: Normal beta users should see **Kyra**, **local mode**, **hybrid / online assist**, and **enhanced with online model** where appropriate — not raw vendor or model branding as the “speaker.”

## Install / testing

Use the checklist **`docs/KYRA-MANUAL-QA-v1.1.12-rc.6.md`** (updated for human testers) together with your usual beta checklist. Run System Intelligence before machine-specific prompts.

## Known limitations

- Live feeds (weather, news, prices) need future tool integrations and operator setup.
- Some edge routing may still expand in later releases.

## Artifact

Installer: `dist/installer/ForgerEMS-Setup-v1.1.12-rc.7.exe` (after running `tools/build-release.ps1` for this version).
