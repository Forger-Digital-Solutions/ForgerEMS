# ForgerEMS User Consent Flow

Applies to: ForgerEMS `v1.2.3-preview.1` public preview

## First-run Terms Gate

On first launch without a current local consent record, ForgerEMS shows a Terms of Use gate before the main shell tools are usable. The main shell is disabled and normal startup initialization is deferred until acceptance.

Users can open:

- Terms of Use
- Privacy / Data Handling
- Legal Notices
- Third-party notices
- About

Required checkbox:

`I have read and agree to the ForgerEMS Terms of Use and understand the Privacy/Data Handling notes.`

Kyra/support/export warning checkbox:

`I understand that logs, support bundles, Kyra context, and exported reports may contain local device/context information. I will review exported files before sharing them.`

Both checkbox notices render as wrapped multi-line text inside the gate so they stay fully readable at 1366x768. The gate header shows the document revision date and the ForgerEMS version it applies to (`Document revision: 2026-07-02 · Applies to ForgerEMS v1.2.3-preview.1`).

## Local Acceptance Record

Acceptance is stored locally at:

`%LOCALAPPDATA%\ForgerEMS\Runtime\config\terms-consent.json`

The record includes terms version, accepted UTC timestamp, app version/build, and a SHA-256 hash of the in-app Terms text.

Current terms version:

`2026-07-02.v1.2.3-preview.1`

If the terms version or terms hash changes, ForgerEMS prompts again.

## Separate Export Consent

Terms acceptance does not authorize sharing local context. Kyra memory export, Kyra Intelligence memory export, and support bundle creation show a separate confirmation before packaging logs or local context.
