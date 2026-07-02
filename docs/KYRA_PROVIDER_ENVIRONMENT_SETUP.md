# Kyra — provider & environment setup (operators / developers)

**Read this first**

| Audience | What you need to know |
|----------|------------------------|
| **Beta testers** | **You do not need API keys** for the normal app. **Kyra works offline** with built-in rules and local reports. Optional Gateway, BYOK, and local AI providers live in **Kyra AI Settings**. |
| **Developers & operators** | This page describes **optional** online or local-server providers, protected/session BYOK storage, and **Windows environment variables** for machines **you** control. |

**Kyra** is the in-app assistant (user-facing name throughout the product).

---

## What Kyra can help with (typical)

- **System diagnostics help** — explaining checklist items and local scan results in plain language.  
- **USB guidance** — benchmark and mapping flows, partition choice, “what to try next” on a bench.  
- **Upgrade and release orientation** — pointing to **GitHub Releases**, checksum habits, and in-app update settings (not live scraping of random sites).  
- **Toolkit / manifest help** — what “Manual Required” means, how refresh health works, where logs live.

---

## Honest limitations

- **No general live web browsing** — Kyra is not a full browser replacement.  
- **No live weather, stock tickers, or news** unless a **dedicated in-app tool** integrates that data source.  
- May **suggest** external documentation or vendor pages when that is the right answer — **you** still choose what to open and trust.

---

## Research Mode

Kyra Research Mode handles current or time-sensitive requests. Current/realtime intents include crypto prices/trends, stocks/finance, weather, news, sports/current scores where supported, software versions, driver/version lookups, Ventoy/latest tool versions, resale/current market pricing, current Windows issues, security advisories/CVEs, and general web/current research.

Routing rule:

1. Use the relevant live tool/provider/research path first.
2. If unavailable, clearly say the live tool/provider is unavailable, rate-limited, or not configured.
3. Do not fabricate current data.
4. Do not answer prices, news, or versions with stale “knowledge cutoff” language.

Live data limitations are expected in beta. Crypto can use the configured crypto provider/CoinGecko path. Stocks require a configured finance provider. Weather uses Open-Meteo/configured weather provider. News, resale comps, driver/vendor lookups, and general web research require matching provider/tool support; otherwise Kyra should be honest that live research is unavailable.

### Hardware part research

Prompts about replacement parts, prices, current availability, official compatibility, service manuals, current docs, or “find/search/look up” should be classified as research-required. The local System Intelligence JSON supplies device facts only; it must not be treated as proof of external compatibility.

For battery questions, especially Dell systems, source priority is:

1. OEM support, service manual, or official parts page.
2. Trustworthy references for OEM-compatible part numbers.
3. Reputable sellers only as secondary availability references.

If the gateway or live tools are unavailable, Kyra should say that clearly and provide only local facts plus verification guidance: match voltage, watt-hour rating, connector, shape, Dell service/manual compatibility, and the physical battery label before buying.

---

## Kyra Intelligence privacy controls

Kyra Intelligence Network is **Local-first repair memory + optional anonymous community learning**.

- Default is **Local Only**.
- Local Kyra Memory stores sanitized machine-scoped repair notes on this PC.
- Anonymous community intelligence sharing is off by default and requires opt-in.
- This phase has no live community upload endpoint; the client is disabled/no-op and can only produce sanitized preview/export data.
- Users can opt out, export Kyra memory, or delete Kyra memory from **Settings → Kyra Assistant**.

ForgerEMS does not sell user data. Local Kyra Memory stays on this PC unless the user explicitly enables a future sharing option. Realtime Kyra Gateway sends only sanitized request context needed to answer current-data questions. Provider API keys are stored server-side and are not included in the desktop app. Anonymous Community Intelligence sharing is optional and off by default.

---

## Offline (default for beta)

- Choose **Offline Local** (or equivalent) in Kyra mode.  
- No cloud API key environment variables are required.  
- Run **System Intelligence** first if you want Kyra to reason about **this** PC’s latest local report.

---

## How credentials are resolved

When online providers are enabled:

1. **Session-only** provider credential entered for the current run (highest priority; kept **in memory** until app close).
2. **Protected saved key** from Kyra AI Settings when Windows protected local storage is available.
3. **Process** environment variable
4. **User** environment variable
5. **Machine** environment variable
6. **Gateway/local/offline fallback** when no direct BYOK credential is usable.

Saved BYOK keys are not written to normal `appsettings` or `copilot-settings.json`; the settings file only records non-secret provider choices such as enabled state, selected model, base URL, storage mode, and last test status. If protected storage fails, Kyra falls back to session-only and warns the user.

After changing **user** or **machine** variables in Windows, use **Refresh Status** in Kyra AI Settings so the app re-reads credentials without a full restart.

---

## Confirm mode in the app

- Open the **Kyra** area → check the **mode** and **provider** indicator (Offline Local vs online vs hybrid).  
- **Kyra AI Settings** shows Overview, Providers, Bring Your Own Key, Live Tools, Privacy & Context, Local AI, and Diagnostics. Normal tabs avoid raw routing internals; technical diagnostics are behind a collapsed expander and are sanitized.

---

## ForgerEMS Beta Gateway

For beta builds, ForgerEMS can route Kyra through a small HTTPS gateway so testers get limited real API time without receiving third-party provider API keys.

- The desktop app calls `FORGEREMS_KYRA_GATEWAY_URL`.
- The desktop app may store a revocable `FORGEREMS_KYRA_GATEWAY_BETA_TOKEN`.
- **Realtime research** uses `POST /v1/kyra/research`; **status** uses `GET /v1/kyra/status`. See [gateway/GATEWAY_RESEARCH_CONTRACT.md](../gateway/GATEWAY_RESEARCH_CONTRACT.md).
- Provider secrets such as OpenAI, OpenRouter, Groq, Gemini, Anthropic, Mistral, Cerebras, or GitHub tokens belong only in the server-side gateway secret store.
- Gateway context sharing is off unless both `FORGEREMS_KYRA_SHARE_SYSTEM_CONTEXT=true` and `FORGEREMS_KYRA_GATEWAY_SHARE_SYSTEM_CONTEXT=true`.
- Local/offline fallback remains available when the gateway is missing, rate-limited, timed out, or down.

```powershell
Set-FdsEnv "FORGEREMS_KYRA_PROVIDER" "forgerems-gateway"
Set-FdsEnv "FORGEREMS_KYRA_ONLINE_ENABLED" "true"
Set-FdsEnv "FORGEREMS_KYRA_GATEWAY_URL" "https://REPLACE_ME.workers.dev"
Set-FdsEnv "FORGEREMS_KYRA_GATEWAY_BETA_TOKEN" "REPLACE_WITH_BETA_ACCESS_TOKEN"
Set-FdsEnv "FORGEREMS_KYRA_GATEWAY_TIMEOUT_SECONDS" "60"
Set-FdsEnv "FORGEREMS_KYRA_GATEWAY_SHARE_SYSTEM_CONTEXT" "false"
Set-FdsEnv "FORGEREMS_KYRA_GATEWAY_ENABLED" "true"
Set-FdsEnv "FORGEREMS_KYRA_RESEARCH_ENABLED" "false"
Set-FdsEnv "FORGEREMS_KYRA_GATEWAY_REQUIRE_CONSENT" "false"
```

Do not put provider API keys in the desktop app, installer, release ZIP, registry defaults, docs, source code, or tester Windows User env vars.

For BYOK providers, use **Kyra AI Settings → Bring Your Own Key**. Use **Use until app closes** for session-only credentials, or **protected local key** when available. Environment variable setup remains available under **Advanced environment setup** for operators who prefer Windows-scoped deployment.

### Rotation / revoke

- Rotate the worker secret `BETA_GATEWAY_TOKEN` and redeploy the worker.
- Update beta user token distribution out-of-band.
- Revoke compromised tokens by replacing/removing them server-side; old tokens should return 401.

### Troubleshooting (redacted-only)

- `401/403`: token mismatch/revoked or missing worker secret.
- `429`: beta limit reached.
- `413`: request too large.
- `500/503`: provider secret missing or provider outage.

Never print beta token values, provider key values, request auth headers, or raw secret-bearing logs.

---

## LM Studio (local OpenAI-compatible server)

- Install [LM Studio](https://lmstudio.ai/) and start a local server.  
- Default base URL in ForgerEMS: **`http://localhost:1234/v1`** (OpenAI-compatible `/v1` + `/chat/completions`).  
- Match the model name in Kyra’s provider configuration to what LM Studio has loaded.  
- **No cloud API key** is required for pure local LM Studio.

**If Kyra says not reachable:** confirm LM Studio is listening, the port matches the base URL, and Windows Firewall is not blocking localhost.

---

## Ollama (local)

- Install [Ollama](https://ollama.com/) and pull a model.  
- Default base URL: **`http://localhost:11434`**.  
- Set the **model name** to match what `ollama list` shows.  
- **No cloud API key** for local Ollama.

**If Kyra says not reachable:** run `ollama serve`, verify `http://localhost:11434/api/tags` responds in a browser.

---

## OpenAI-compatible (cloud or self-hosted)

Many providers expose OpenAI-style `/v1/chat/completions`.

- Default OpenAI cloud base URL: **`https://api.openai.com/v1`**  
- Typical env var for a cloud key: **`OPENAI_API_KEY`** (operator sets this on the machine or process — **not** a beta tester checklist item).  
- Kyra AI Settings allows **base URL** and **model** overrides for compatible gateways and BYOK providers.

**Wrong base URL** → 404 or connection errors. **Wrong model** → “model not found”. **Bad key** → 401.

---

## Other cloud slots (operator reference)

When your organization enables these integrations, keys are supplied **outside** normal tester flows (environment or secure deployment tooling):

| Provider (typical) | Env var |
|--------------------|---------|
| Google Gemini | `GEMINI_API_KEY` |
| Anthropic | `ANTHROPIC_API_KEY` |
| Groq | `GROQ_API_KEY` |
| OpenRouter | `OPENROUTER_API_KEY` |
| Cerebras | `CEREBRAS_API_KEY` |
| Mistral | `MISTRAL_API_KEY` |
| GitHub Models | `GITHUB_MODELS_TOKEN` |

Use **Kyra AI Settings** to enable the matching slot where applicable, then **Refresh Status**.

GitHub Models can also route across optional model slots with the same token:

```powershell
Set-FdsEnv "GITHUB_MODELS_TOKEN" "PASTE_YOUR_REAL_GITHUB_MODELS_PAT"
Set-FdsEnv "FORGEREMS_GITHUB_MODELS_DEFAULT_MODEL" "openai/gpt-5"
Set-FdsEnv "FORGEREMS_GITHUB_MODELS_FAST_MODEL" "deepseek/DeepSeek-V3-0324"
Set-FdsEnv "FORGEREMS_GITHUB_MODELS_ALT_MODEL" "meta/Llama-4-Scout-17B-16E-Instruct"
```

The three `FORGEREMS_GITHUB_MODELS_*_MODEL` values are model IDs, not secrets. One GitHub Models token works across the configured model IDs your account can access. Restart ForgerEMS or refresh provider status after setting Windows User environment variables.

---

## Cloudflare Workers AI

- **`CLOUDFLARE_API_KEY`** **and** **`CLOUDFLARE_ACCOUNT_ID`** are both required when this integration is used.  
- Set at **user** or **machine** level for the deployment, then **Refresh Provider Status**.

---

## Example: persistent Windows **user** environment (operators only)

Run **cmd.exe** as the normal user. Replace the placeholder with a real secret from your vault — **never** paste real keys into chat, tickets, or email:

```cmd
setx GEMINI_API_KEY REPLACE_ME
```

Close and reopen ForgerEMS, then **Refresh Status** in Kyra AI Settings.

**Remove a user var:** Windows Settings → System → About → **Advanced system settings** → Environment Variables → User variables → delete the row.

---

## Example: PowerShell **process-only** session (operators)

For a **single terminal session**, set the variable without persisting it to the user profile, then launch ForgerEMS from that same window. Prefer reading the secret from a secure prompt rather than embedding it in scripts that might be logged:

```powershell
$name = "GROQ" + "_API_KEY"
$secret = Read-Host "API key" -AsSecureString
$BSTR = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
    [Environment]::SetEnvironmentVariable($name, $plain, "Process")
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)
}
& "C:\Path\To\ForgerEMS.exe"
```

---

## Troubleshooting

| Symptom | Likely cause | What to try |
|---------|----------------|------------|
| Provider unavailable | No key / wrong env target | Confirm env scope + new app instance + Refresh Provider Status |
| 401 / unauthorized | Bad or revoked key | Rotate at vendor console; never share old key in email |
| Model not found | Typo or model not enabled | Match vendor model id exactly |
| Timeout / rate limit | Network or quota | Retry later; reduce prompt size; stay on Offline Local |
| LM Studio / Ollama unreachable | Service stopped | Start local server; check firewall |

---

## Safety

Never paste real API keys into screenshots, Discord, or support email. Use **sanitized** log excerpts only.

---

## Where this is enforced in code

Provider resolution, environment variable names, default base URLs, and HTTP clients live in the **Kyra / provider** area of the WPF solution (`ProviderEnvironmentResolver`, provider configuration types, and related services). When in doubt, keep customer benches on **Offline Local**.
