# ForgerEMS Kyra Gateway - Cloudflare Worker Starter

This folder is a server-side starter for beta Kyra API access. Do not put real provider API keys in this repository, desktop app, release ZIPs, installer defaults, appsettings, `.env.example`, docs, or registry defaults.

## Deployed URL (example)

`https://forgerems-kyra-gateway.forgerdigitalsolutions.workers.dev`

Use your own deployed URL for production/beta rollout.

## Desktop app credentials (beta-shippable)

The desktop app should only use:

- `FORGEREMS_KYRA_GATEWAY_URL`
- `FORGEREMS_KYRA_GATEWAY_BETA_TOKEN`

Provider API keys must stay server-side only as Worker secrets.

## Worker secret setup

```powershell
npx wrangler@latest login
npx wrangler@latest secret put BETA_GATEWAY_TOKEN
npx wrangler@latest secret put OPENAI_API_KEY
npx wrangler@latest secret put OPENROUTER_API_KEY
npx wrangler@latest secret put GROQ_API_KEY
```

Optional provider secrets can be added later, server-side only.

## Deploy

1. Copy `wrangler.example.toml` to `wrangler.toml`.
2. Configure non-secret vars only (`DEFAULT_MODEL`, `MAX_REQUEST_BYTES`, timeout and optional rate-limit flags).
3. Add secrets with `npx wrangler@latest secret put ...`.
4. Deploy with:

```powershell
npx wrangler@latest deploy
```

## Current protections

- Beta token validation.
- Request size cap (`MAX_REQUEST_BYTES`).
- Provider timeout cap (`PROVIDER_TIMEOUT_MS`).
- Sanitized error responses only (no token/body/provider key output).
- No request-body logging.
- No token logging.
- No provider-key logging.
- OpenAI-compatible server-side fallback chain.

## Rate limiting status

Current beta gateway has token validation and request-size limits; durable per-token/per-IP rate limiting should be enabled before broad public beta.

Scaffold variables are included now:

- `RATE_LIMITS_ENABLED` (`true|false`)
- `BETA_DAILY_TOKEN_LIMIT`
- `BETA_DAILY_IP_LIMIT`
- `RATE_LIMIT_KV` (KV binding)

When `RATE_LIMITS_ENABLED=false` (default), the code path is safely disabled and does not claim durable rate limiting.

## Rotate / revoke beta token

Rotate:

```powershell
npx wrangler@latest secret put BETA_GATEWAY_TOKEN
npx wrangler@latest deploy
```

Revoke quickly:

1. Replace `BETA_GATEWAY_TOKEN` with a new value.
2. Push updated beta token to allowed clients.
3. Redeploy.
4. Confirm old token now returns 401.

## Troubleshooting (sanitized)

- `401/403`: beta token mismatch, missing `BETA_GATEWAY_TOKEN`, or wrong worker URL.
- `429`: beta cap reached (if rate-limits enabled) or provider-side throttling path.
- `500/503`: no provider secret configured, provider failure, or upstream outage.
- `413`: request too large (`MAX_REQUEST_BYTES` too low for payload).

Never print request bodies, auth headers, beta tokens, or provider keys when debugging.
