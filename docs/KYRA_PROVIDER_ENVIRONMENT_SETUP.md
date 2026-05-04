# Kyra — provider & environment setup (operators / developers)

**Read this first**

| Audience | What you need to know |
|----------|------------------------|
| **Beta testers** | **You do not need API keys** for the normal app. **Kyra works offline** with built-in rules and local reports. The **standard beta download does not ask you to configure third-party API keys.** |
| **Developers & operators** | This page describes **optional** online or local-server providers and **Windows environment variables** for machines **you** control. |

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

## Offline (default for beta)

- Choose **Offline Local** (or equivalent) in Kyra mode.  
- No cloud API key environment variables are required.  
- Run **System Intelligence** first if you want Kyra to reason about **this** PC’s latest local report.

---

## How credentials are resolved (operator)

When online providers are enabled for a deployment:

1. **Session-only** provider credential entered for the current run (highest priority; kept **in memory**, not written to ordinary settings JSON).  
2. **Process** environment variable  
3. **User** environment variable  
4. **Machine** environment variable  

After changing **user** or **machine** variables in Windows, use **Refresh Provider Status** in Kyra Advanced so the app re-reads the environment without a full restart.

---

## Confirm mode in the app

- Open the **Kyra** area → check the **mode** and **provider** indicator (Offline Local vs online vs hybrid).  
- **Kyra Advanced** shows which provider is active, base URL, model, and redaction / data-sharing toggles.

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
- Kyra Advanced may allow **base URL** and **model** overrides for compatible gateways.

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

Use **Kyra Advanced** to enable the matching slot where applicable, then **Refresh Provider Status**.

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

Close and reopen ForgerEMS, then **Refresh Provider Status** in Kyra Advanced.

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
