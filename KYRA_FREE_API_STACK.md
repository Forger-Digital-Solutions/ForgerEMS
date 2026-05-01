# Kyra Free API Stack

Kyra is designed to stay useful with zero paid API budget.

**Beta v1.1.4:** Offline Local Kyra remains the default; free API pool is optional; session-only API key handling and “context sharing OFF by default” behavior are unchanged from prior beta notes below.

## Core Rules

- Offline Local Kyra is always available and is the safe default fallback.
- Paid/BYOK providers are optional and disabled unless enabled by the user.
- Free provider calls must respect provider terms, rate limits, and cooldown windows.
- Kyra never rotates fake accounts, never bypasses quotas, and never abuses trials.

## Default Provider Priority

1. Local Offline
2. Gemini (free tier)
3. Groq (free tier)
4. Cerebras (free tier)
5. OpenRouter Free
6. Mistral (free/eval)
7. GitHub Models
8. Cloudflare Workers AI
9. BYOK providers (OpenAI, Anthropic)
10. ForgerEMS Cloud (future placeholder)

## Privacy And Context Sharing

- `AllowOnlineSystemContextSharing` defaults to `false`.
- When disabled, online providers receive user prompt only.
- When enabled, Kyra sends sanitized summary only:
  - CPU, GPU, RAM, storage, OS, general health hints.
- Kyra never sends serials, usernames, profile paths, raw logs, tokens, or keys.

## Quota And Rate-Limit Behavior

- Tracks per-provider state:
  - configured/enabled
  - last success/failure
  - failure reason
  - daily requests
  - timeout/error counts
  - cooldown timestamp
- Handles failure classes:
  - 401/403 -> auth failed
  - 429 -> rate limited + cooldown
  - 5xx -> temporary unavailable
  - timeout/network -> transient failure, try next provider
- Applies local app caps:
  - per-provider daily cap
  - max input chars
  - max output tokens
  - max fallbacks per message

## API Key Handling

- Session key store is supported now (`KyraApiKeyStore`), not written to plain JSON.
- Environment-variable key lookup follows **process → user → machine** scope (session keys override env). Use **Refresh Provider Status** in Kyra Advanced after changing user/machine variables so the app re-reads them without a restart.
- Cloudflare Workers AI needs **`CLOUDFLARE_API_KEY`** and **`CLOUDFLARE_ACCOUNT_ID`**.
- UI displays masked keys only (`xxxx...yyyy`).
- Persistent secure storage (DPAPI/Credential Manager) is still pending.
- Beta note: this build uses **session-only key storage** for entered keys.
- Do not paste API keys into chat; use provider settings fields only.

## Beta Tester Setup (Quick)

1. Open Kyra provider settings.
2. Keep mode at `Offline Local` if you do not want online providers.
3. For free API testing, switch to `Free API Pool`.
4. Enable one provider (for example Gemini/Groq/OpenRouter).
5. Add either:
   - session key in provider settings, or
   - environment variable key name + existing env var value.
6. Keep `AllowOnlineSystemContextSharing` off unless you explicitly want sanitized specs included.
7. Send a general prompt and verify status line shows provider/fallback behavior.

## Provider Status Meanings

- `Disabled`: provider toggle is off.
- `Not configured`: enabled but no usable key/config.
- `Configured`: enabled and key/config detected.
- `Placeholder`: known provider slot, adapter not fully implemented for this beta.
- `Fallback: Local Kyra active`: online provider path was unavailable and Local Kyra answered.

## Implemented vs Placeholder Providers

- Implemented:
  - Local Offline Rules
  - Gemini provider adapter
  - OpenAI-compatible adapters for Groq/Cerebras/OpenRouter/Mistral/GitHub Models/Cloudflare
  - OpenAI-compatible generic BYOK provider
- Placeholder:
  - Hugging Face Inference Providers
  - ForgerEMS Cloud provider
  - Anthropic provider shell remains adapter-stub
  - Some OpenAI-compatible endpoints may require per-provider endpoint/model tuning

## Testing Strategy

- Unit tests use fake providers only (no live API calls).
- Validate:
  - provider pool routing/fallback
  - safety block before online calls
  - response caching avoids duplicate provider calls
  - key masking behavior

## Future ForgerEMS Cloud

- Architecture reserves `ForgerEMSCloud` provider kind.
- Desktop app does not include broker billing or owner keys.
- Cloud broker implementation should live server-side only.

---

Free provider availability, limits, and terms can change. Kyra must handle failure gracefully.
