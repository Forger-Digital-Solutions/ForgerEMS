export interface Env {
  BETA_GATEWAY_TOKEN: string;
  OPENAI_API_KEY?: string;
  OPENROUTER_API_KEY?: string;
  GROQ_API_KEY?: string;
  RELEASE_CHANNEL?: string;
  DEFAULT_MODEL?: string;
  MAX_REQUEST_BYTES?: string;
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
      const result = await callOpenAiCompatible(provider, body, userContent);
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
): Promise<{ ok: true; message: string } | { ok: false; errorCode: string }> {
  const response = await fetch(`${provider.baseUrl}/chat/completions`, {
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
  });

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
