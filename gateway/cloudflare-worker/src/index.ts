export interface Env {
  BETA_GATEWAY_TOKEN: string;
  OPENAI_API_KEY?: string;
  OPENROUTER_API_KEY?: string;
  GROQ_API_KEY?: string;
  RELEASE_CHANNEL?: string;
  DEFAULT_MODEL?: string;
  MAX_REQUEST_BYTES?: string;
  PROVIDER_TIMEOUT_MS?: string;
  BETA_DAILY_TOKEN_LIMIT?: string;
  BETA_DAILY_IP_LIMIT?: string;
  RATE_LIMITS_ENABLED?: string;
  RATE_LIMIT_KV?: KVNamespace;
}

type KyraGatewayRequest = {
  appVersion?: string;
  releaseChannel?: string;
  licenseTier?: string;
  betaToken?: string;
  conversationId?: string;
  messageId?: string;
  userMessage?: string;
  personality?: string;
  intent?: string;
  toolsRequested?: string[];
  machineContext?: Record<string, string>;
  memorySummary?: string;
  maxTokens?: number;
  temperature?: number;
};

type KyraGatewayResponse = {
  ok: boolean;
  providerUsed?: string;
  modelUsed?: string;
  message: string;
  toolResults?: unknown[];
  fallbackUsed?: boolean;
  rateLimit?: {
    remainingToday?: number;
    resetUtc?: string;
  };
  diagnosticNote?: string;
  errorCode?: string;
  retryAfterSeconds?: number;
};

const jsonHeaders = {
  "content-type": "application/json; charset=utf-8",
  "cache-control": "no-store",
};

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    if (request.method !== "POST") {
      return json({ ok: false, errorCode: "MethodNotAllowed", message: "POST required." }, 405);
    }

    const contentLength = Number(request.headers.get("content-length") ?? "0");
    const maxBytes = Number(env.MAX_REQUEST_BYTES ?? "18000");
    if (contentLength > maxBytes) {
      return json({ ok: false, errorCode: "RequestTooLarge", message: "Kyra request is too large for the beta gateway." }, 413);
    }

    let body: KyraGatewayRequest;
    try {
      body = await request.json<KyraGatewayRequest>();
    } catch {
      return json({ ok: false, errorCode: "BadJson", message: "Invalid gateway request." }, 400);
    }

    if (!env.BETA_GATEWAY_TOKEN || body.betaToken !== env.BETA_GATEWAY_TOKEN) {
      return json({ ok: false, errorCode: "Unauthorized", message: "Kyra beta gateway token is missing or invalid." }, 401);
    }

    const rateLimit = await evaluateRateLimit(request, env, body.betaToken);
    if (!rateLimit.ok) {
      return json(
        {
          ok: false,
          errorCode: "BetaLimitReached",
          message: "Kyra beta API time is used up for today. Local/offline mode is still available.",
          retryAfterSeconds: rateLimit.retryAfterSeconds,
          rateLimit: {
            remainingToday: 0,
            resetUtc: rateLimit.resetUtc,
          },
        },
        429,
      );
    }

    const prompt = sanitizeForProvider(body.userMessage ?? "");
    if (!prompt) {
      return json({ ok: false, errorCode: "EmptyPrompt", message: "Kyra needs a prompt to answer." }, 400);
    }

    const context = body.machineContext?.summary
      ? `\n\nSanitized machine context:\n${sanitizeForProvider(body.machineContext.summary)}`
      : "";
    const memory = body.memorySummary ? `\n\nSafe memory summary:\n${sanitizeForProvider(body.memorySummary)}` : "";
    const userContent = `${prompt}${context}${memory}`.slice(0, maxBytes);

    const providers = [
      env.OPENAI_API_KEY
        ? { name: "openai", baseUrl: "https://api.openai.com/v1", key: env.OPENAI_API_KEY, model: env.DEFAULT_MODEL || "gpt-4o-mini" }
        : null,
      env.OPENROUTER_API_KEY
        ? { name: "openrouter", baseUrl: "https://openrouter.ai/api/v1", key: env.OPENROUTER_API_KEY, model: "openrouter/auto" }
        : null,
      env.GROQ_API_KEY
        ? { name: "groq", baseUrl: "https://api.groq.com/openai/v1", key: env.GROQ_API_KEY, model: "llama-3.1-8b-instant" }
        : null,
    ].filter(Boolean) as Array<{ name: string; baseUrl: string; key: string; model: string }>;

    if (providers.length === 0) {
      return json({ ok: false, errorCode: "NoProviderConfigured", message: "Kyra gateway has no server-side provider configured." }, 503);
    }

    let lastError = "unknown";
    for (let i = 0; i < providers.length; i++) {
      const provider = providers[i];
      const result = await callOpenAiCompatible(provider, body, userContent, Number(env.PROVIDER_TIMEOUT_MS ?? "45000"));
      if (result.ok) {
        return json({
          ok: true,
          providerUsed: provider.name,
          modelUsed: provider.model,
          message: result.message,
          toolResults: [],
          fallbackUsed: i > 0,
          diagnosticNote: "gateway response",
        });
      }

      lastError = result.errorCode;
    }

    return json({
      ok: false,
      errorCode: "GatewayProviderFailure",
      message: "Kyra gateway providers are unavailable. Local/offline mode is still available.",
      diagnosticNote: lastError,
    }, 503);
  },
};

async function callOpenAiCompatible(
  provider: { baseUrl: string; key: string; model: string },
  body: KyraGatewayRequest,
  userContent: string,
  providerTimeoutMs: number,
): Promise<{ ok: true; message: string } | { ok: false; errorCode: string }> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort("provider-timeout"), clamp(providerTimeoutMs, 1500, 90000));
  let response: Response;
  try {
    response = await fetch(`${provider.baseUrl}/chat/completions`, {
      method: "POST",
      headers: {
        "authorization": `Bearer ${provider.key}`,
        "content-type": "application/json",
      },
      body: JSON.stringify({
        model: provider.model,
        messages: [
          {
            role: "system",
            content: `You are Kyra, the ForgerEMS beta assistant. Be concise, useful, and do not ask for secrets. Personality: ${body.personality ?? "bubbly-tech"}.`,
          },
          { role: "user", content: userContent },
        ],
        max_tokens: Math.min(Math.max(body.maxTokens ?? 1000, 128), 2048),
        temperature: Math.min(Math.max(body.temperature ?? 0.5, 0), 1),
      }),
      signal: controller.signal,
    });
  } catch {
    return { ok: false, errorCode: "ProviderTimeoutOrNetwork" };
  } finally {
    clearTimeout(timeout);
  }

  if (!response.ok) {
    return { ok: false, errorCode: `HTTP_${response.status}` };
  }

  const data = await response.json<{ choices?: Array<{ message?: { content?: string } }> }>();
  const message = data.choices?.[0]?.message?.content?.trim();
  return message ? { ok: true, message } : { ok: false, errorCode: "EmptyProviderResponse" };
}

function json(payload: KyraGatewayResponse, status = 200): Response {
  return new Response(JSON.stringify(payload), { status, headers: jsonHeaders });
}

function sanitizeForProvider(value: string): string {
  return value
    .replace(/(api[_-]?key|token|secret|password)\s*[:=]\s*["']?[^"'\s;]+/gi, "[redacted]")
    .replace(/\bsk-[A-Za-z0-9_-]{12,}\b/gi, "[redacted]")
    .replace(/\b(ghp|gho|github_pat)_[A-Za-z0-9_]{20,}\b/gi, "[redacted]")
    .replace(/[A-Za-z]:\\Users\\[^\s\r\n]+/g, "[private path redacted]")
    .slice(0, 16000);
}

function clamp(value: number, min: number, max: number): number {
  if (!Number.isFinite(value)) {
    return min;
  }

  return Math.min(Math.max(value, min), max);
}

async function evaluateRateLimit(
  request: Request,
  env: Env,
  betaToken: string | undefined,
): Promise<{ ok: true } | { ok: false; retryAfterSeconds?: number; resetUtc?: string }> {
  const enabled = String(env.RATE_LIMITS_ENABLED ?? "false").toLowerCase() === "true";
  const kv = env.RATE_LIMIT_KV;
  if (!enabled || !kv || !betaToken) {
    return { ok: true };
  }

  const tokenLimit = Number(env.BETA_DAILY_TOKEN_LIMIT ?? "0");
  const ipLimit = Number(env.BETA_DAILY_IP_LIMIT ?? "0");
  if (tokenLimit <= 0 && ipLimit <= 0) {
    return { ok: true };
  }

  const ip = request.headers.get("CF-Connecting-IP") ?? "unknown";
  const now = new Date();
  const day = `${now.getUTCFullYear()}-${String(now.getUTCMonth() + 1).padStart(2, "0")}-${String(now.getUTCDate()).padStart(2, "0")}`;
  const resetUtc = `${day}T23:59:59Z`;
  const tokenHash = await sha256Hex(betaToken);
  const ipHash = await sha256Hex(ip);

  const tokenKey = `rl:token:${day}:${tokenHash}`;
  const ipKey = `rl:ip:${day}:${ipHash}`;
  const ttl = Math.max(60, Math.floor((Date.parse(resetUtc) - now.getTime()) / 1000));

  const tokenCount = tokenLimit > 0 ? await incrementDailyCounter(kv, tokenKey, ttl) : 0;
  const ipCount = ipLimit > 0 ? await incrementDailyCounter(kv, ipKey, ttl) : 0;
  if ((tokenLimit > 0 && tokenCount > tokenLimit) || (ipLimit > 0 && ipCount > ipLimit)) {
    return { ok: false, retryAfterSeconds: ttl, resetUtc };
  }

  return { ok: true };
}

async function incrementDailyCounter(kv: KVNamespace, key: string, ttlSeconds: number): Promise<number> {
  const currentRaw = await kv.get(key);
  const current = Number(currentRaw ?? "0");
  const next = Number.isFinite(current) && current > 0 ? current + 1 : 1;
  await kv.put(key, String(next), { expirationTtl: ttlSeconds });
  return next;
}

async function sha256Hex(input: string): Promise<string> {
  const encoded = new TextEncoder().encode(input);
  const digest = await crypto.subtle.digest("SHA-256", encoded);
  const bytes = new Uint8Array(digest);
  return [...bytes].map((b) => b.toString(16).padStart(2, "0")).join("");
}
