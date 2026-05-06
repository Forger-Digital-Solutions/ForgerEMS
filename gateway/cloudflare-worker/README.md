# ForgerEMS Kyra Gateway - Cloudflare Worker Starter

This folder is a server-side starter for beta Kyra API access. Do not put real provider API keys in this repository, the desktop app, release ZIPs, installers, appsettings, `.env.example`, docs, or registry defaults.

The desktop app only calls the gateway with:

- `FORGEREMS_KYRA_GATEWAY_URL`
- `FORGEREMS_KYRA_GATEWAY_BETA_TOKEN`

Provider API keys live only as Worker secrets:

```powershell
wrangler secret put BETA_GATEWAY_TOKEN
wrangler secret put OPENAI_API_KEY
wrangler secret put OPENROUTER_API_KEY
wrangler secret put GROQ_API_KEY
```

Add other provider secrets only on the server side. Use tight provider-side and gateway-side quota controls for public beta tokens, and rotate/revoke tokens when needed.

## Deploy Outline

1. Copy `wrangler.example.toml` to `wrangler.toml`.
2. Set the Worker name and any non-secret vars.
3. Add secrets with `wrangler secret put ...`.
4. Deploy with `wrangler deploy`.
5. Set the desktop User env vars to the deployed Worker URL and beta token.

The starter rejects oversized requests, validates the beta token, avoids logging request bodies or tokens, and returns the standardized `KyraGatewayResponse` shape. It includes a simple OpenAI-compatible fallback chain; extend it server-side only.
