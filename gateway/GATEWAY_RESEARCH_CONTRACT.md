# ForgerEMS Kyra Gateway — Research API contract

The desktop app calls a **ForgerEMS Kyra Gateway** (Cloudflare Worker). **Provider API keys exist only as Worker secrets** (`wrangler secret put`); they must not appear in app binaries, `release.json`, docs, logs, or `copilot-settings.json`.

## Research: `POST /v1/kyra/research`

**Headers**

- `Authorization: Bearer <beta or session token>` (optional legacy: `betaToken` in JSON body — prefer header)
- `Content-Type: application/json`
- `X-ForgerEMS-Version: <app version>`
- `X-ForgerEMS-Channel: <release channel>`

**Body (JSON)**

```json
{
  "requestId": "opaque-id",
  "intent": "crypto|weather|finance|news|web|software_version|driver_lookup|resale_comps|sports|chat|hardware_part_lookup",
  "prompt": "sanitized user prompt",
  "context": {
    "machineClass": "optional broad class",
    "healthScoreBand": "optional",
    "issueCategory": "optional",
    "usbState": "optional",
    "privacyMode": "local-only|sanitized-gateway",
    "manufacturer": "optional sanitized vendor",
    "modelFamily": "optional sanitized model line (no service tag/serial)",
    "partCategory": "optional: battery|memory|storage|charger|dock|other",
    "knownLocalFacts": "optional coarse bands only, e.g. storage bus, battery wear band, memory type band"
  },
  "consent": {
    "gatewayResearch": true,
    "communitySharing": false
  }
}
```

**Success**

```json
{
  "ok": true,
  "answer": "...",
  "tool": "crypto",
  "provider": "coingecko|openai|...",
  "freshnessUtc": "...",
  "confidence": "high|medium|low",
  "sources": [],
  "metadata": { "liveResearch": true, "sanitizedContext": true }
}
```

**Failure**

```json
{
  "ok": false,
  "errorCode": "provider_unavailable|rate_limited|not_configured|timeout|unauthorized|consent_required",
  "safeMessage": "User-safe explanation without secrets."
}
```

## Status: `GET /v1/kyra/status`

Same `Authorization: Bearer` as research. Returns **only** coarse readiness flags (e.g. `configured` / `unconfigured`), never secret values.

```json
{
  "ok": true,
  "providers": {
    "aiChat": "configured",
    "crypto": "configured",
    "weather": "configured",
    "finance": "unconfigured",
    "news": "unconfigured",
    "webResearch": "configured"
  }
}
```

## `hardware_part_lookup` notes

- Desktop builds this intent only from **sanitized** scan summaries (model family, coarse health/storage/memory/battery bands). It must **not** include service tags, serial numbers, full file paths, raw logs, emails, IPs, or user identifiers.
- Responses should return **candidates**, **compatibility basis**, **confidence**, **source type** (official / manual / retailer), **freshnessUtc**, optional **price range** only when grounded in live retrieval — never invent SKUs or “cheapest” without current data.

## Security expectations

- Store provider keys with **`wrangler secret put`**; do not put them in `wrangler.toml` vars checked into git.
- Support rotation by updating secrets and redeploying.
- Enforce rate limits, request size limits, and timeouts on the Worker.
- Reject missing/invalid beta tokens.
- Redact logs on the Worker; never return provider secrets to clients.
- CORS: add only if browser clients are required; the WPF app is not browser CORS-bound.

## Desktop environment variables (no provider secrets)

- `FORGEREMS_KYRA_GATEWAY_URL` — Worker base URL
- `FORGEREMS_KYRA_GATEWAY_BETA_TOKEN` — revocable gateway token (not an OpenAI/Groq/etc. key)
- `FORGEREMS_KYRA_GATEWAY_TIMEOUT_SECONDS`
- `FORGEREMS_KYRA_GATEWAY_ENABLED` — master disable
- `FORGEREMS_KYRA_GATEWAY_REQUIRE_CONSENT` — require in-app consent
- `FORGEREMS_KYRA_RESEARCH_ENABLED` — default-on path for research when combined with app settings
- `FORGEREMS_KYRA_GATEWAY_SHARE_SYSTEM_CONTEXT` — allow sanitized SI context when the user also enables sharing in app

## Disabling realtime gateway

Set `FORGEREMS_KYRA_GATEWAY_ENABLED=false`, clear gateway URL/token, or turn off **Kyra Realtime Gateway** in Kyra Advanced → Realtime Gateway. Local/offline Kyra remains available.
