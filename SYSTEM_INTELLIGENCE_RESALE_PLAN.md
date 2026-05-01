# System Intelligence + Resale Plan (v1.1.4 Beta)

## Scope

This pass improves machine-read reliability, offline resale estimation, and listing draft generation without requiring API keys, scraping, or paid services.

## Hardware Detection Model

ForgerEMS now models hardware identity through a best-effort reader:

- `ISystemHardwareReader`
- `WindowsHardwareReader`
- `HardwareProbeResult`
- `HardwareProbeStatus`
- `HardwareProbeWarning`
- `DeviceIdentityProfile`
- `HardwareProbeTimeoutPolicy`

Fields are treated as **best-effort**. If probes are partial, UI/chat should show unknown values safely and continue.

## Privacy Rules

Never display/send by default:

- serial numbers/service tags
- usernames or profile paths
- product keys/license keys
- raw logs
- API keys/tokens

Redaction is enforced through `HardwarePrivacyRedactor` (delegates to `CopilotRedactor` patterns).

## Resale Engine Model

Core classes:

- `IResalePricingService`
- `OfflineResaleEstimator`
- `IMarketPricingService`
- `EbayMarketPricingService` (official-API-first status)
- `ManualComparablePricingService`
- `FacebookMarketplacePricingService` (manual/future placeholder)
- `OfferUpPricingService` (manual/future placeholder)
- `ListingPriceEstimate`
- `ResaleConfidenceLevel`
- `ListingDraft`
- `ResaleConditionProfile`
- `DeviceResaleProfile`

## Offline-First Behavior

- Works with zero API keys.
- Uses local hardware profile + condition placeholders.
- Produces: min, quick-sale, fair, stretch, parts prices.
- Includes confidence level + reason.
- Explicitly labels estimate as offline/local where applicable.

## eBay / Marketplace Behavior

- eBay: active comps interface supported as a configured provider path.
- In this beta, if unconfigured, status is explicit: active comps unavailable and sold comps not configured.
- Facebook Marketplace / OfferUp: manual/future source only. No scraping.

## Manual Comparables

Manual comparables support:

- platform
- title
- price
- condition
- notes
- url (optional)

Engine computes median and uses the result as a confidence aid.

## Listing Generator

Listing drafts include:

- title
- short description
- full description
- specs block
- condition notes
- included accessories
- photo checklist

No auto-posting is implemented.

## Beta Validation Focus

- unknown/missing hardware fields do not crash
- privacy redaction removes sensitive strings
- offline estimator runs without API/config
- Kyra resale responses stay honest about offline vs market-data limits
- OfferUp/Facebook requests never return hallucinated live prices
- beta feedback: **ForgerDigitalSolutions@outlook.com** (no secrets in email; see in-app Legal/FAQ)
