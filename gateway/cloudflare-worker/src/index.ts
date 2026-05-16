/// <reference types="@cloudflare/workers-types" />

import type { GatewayEnv } from "./gateway-env";
import { buildCfInferenceSequence, extractChatUserText, selectGatewayModel } from "./kyra-gateway-routing";

export type { GatewayEnv as Env } from "./gateway-env";

type KyraGatewayRequest = {
  appVersion?: string;
  releaseChannel?: string;
  licenseTier?: string;
  betaToken?: string;
  conversationId?: string;
  messageId?: string;
  userMessage?: string;
  /** Standalone-friendly aliases (same semantics as userMessage). */
  message?: string;
  prompt?: string;
  input?: string;
  personality?: string;
  intent?: string;
  toolsRequested?: string[];
  machineContext?: Record<string, string> & { summary?: string };
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

type KyraResearchRequest = {
  requestId?: string;
  intent?: string;
  prompt?: string;
  context?: {
    machineClass?: string;
    healthScoreBand?: string;
    issueCategory?: string;
    usbState?: string;
    privacyMode?: string;
    manufacturer?: string;
    modelFamily?: string;
    partCategory?: string;
    knownLocalFacts?: Record<string, string | undefined>;
  };
  consent?: {
    gatewayResearch?: boolean;
    communitySharing?: boolean;
  };
  betaToken?: string;
};

type KyraResearchResponse = {
  ok: boolean;
  answer?: string;
  tool?: string;
  provider?: string;
  freshnessUtc?: string;
  confidence?: string;
  sources?: unknown[];
  metadata?: { liveResearch?: boolean; sanitizedContext?: boolean };
  errorCode?: string;
  safeMessage?: string;
  retryAfterSeconds?: number;
};

const jsonHeaders = {
  "content-type": "application/json; charset=utf-8",
  "cache-control": "no-store",
};

export default {
  async fetch(request: Request, env: GatewayEnv): Promise<Response> {
    const url = new URL(request.url);
    const pathNorm = url.pathname.replace(/\/$/, "");
    const maxBytes = Number(env.MAX_REQUEST_BYTES ?? "18000");

    if (request.method === "GET" && pathNorm.endsWith("/v1/kyra/status")) {
      return handleGatewayStatus(request, env);
    }

    if (request.method !== "POST") {
      return jsonLegacy({ ok: false, errorCode: "MethodNotAllowed", message: "POST required." }, 405);
    }

    const researchPath = pathNorm.endsWith("/v1/kyra/research");
    const legacyChatPath = pathNorm.endsWith("/v1/kyra/chat");
    if (!researchPath && !legacyChatPath) {
      return jsonLegacy({ ok: false, errorCode: "NotFound", message: "Unknown Kyra gateway path." }, 404);
    }

    let bodyText: string;
    try {
      bodyText = await readBodyTextCapped(request, maxBytes);
    } catch {
      return jsonLegacy({ ok: false, errorCode: "RequestTooLarge", message: "Kyra request is too large for the beta gateway." }, 413);
    }

    if (researchPath) {
      return handleResearchBody(bodyText, request, env, maxBytes);
    }

    return handleLegacyChatBody(bodyText, request, env, maxBytes);
  },
};

async function readBodyTextCapped(request: Request, maxBytes: number): Promise<string> {
  const buf = await request.arrayBuffer();
  if (buf.byteLength > maxBytes) {
    throw new Error("too-large");
  }
  return new TextDecoder("utf-8").decode(buf);
}

function workersAiAvailable(env: GatewayEnv): boolean {
  return typeof env.AI?.run === "function";
}

function buildExternalProviders(env: GatewayEnv): Array<{ name: string; baseUrl: string; key: string; model: string }> {
  const openAiModel = "gpt-4o-mini";
  return [
    env.OPENROUTER_API_KEY
      ? { name: "openrouter", baseUrl: "https://openrouter.ai/api/v1", key: env.OPENROUTER_API_KEY, model: "openrouter/auto" }
      : null,
    env.GROQ_API_KEY
      ? { name: "groq", baseUrl: "https://api.groq.com/openai/v1", key: env.GROQ_API_KEY, model: "llama-3.1-8b-instant" }
      : null,
    env.OPENAI_API_KEY
      ? { name: "openai", baseUrl: "https://api.openai.com/v1", key: env.OPENAI_API_KEY, model: openAiModel }
      : null,
  ].filter(Boolean) as Array<{ name: string; baseUrl: string; key: string; model: string }>;
}

function anyLlmConfigured(env: GatewayEnv): boolean {
  return workersAiAvailable(env) || buildExternalProviders(env).length > 0;
}

async function handleGatewayStatus(request: Request, env: GatewayEnv): Promise<Response> {
  const token = extractBetaToken(request, undefined);
  const expectedToken = normalizeToken(env.BETA_GATEWAY_TOKEN);
  if (!expectedToken || !token || token !== expectedToken) {
    return new Response(JSON.stringify({ ok: false, errorCode: "unauthorized" }), {
      status: 401,
      headers: jsonHeaders,
    });
  }

  const rateLimit = await evaluateRateLimit(request, env, token);
  if (!rateLimit.ok) {
    return new Response(JSON.stringify({ ok: false, errorCode: "rate_limited", retryAfterSeconds: rateLimit.retryAfterSeconds }), {
      status: 429,
      headers: jsonHeaders,
    });
  }

  const cfAi = workersAiAvailable(env) ? "configured" : "unconfigured";
  const ext = buildExternalProviders(env);
  const aiChat = anyLlmConfigured(env) ? "configured" : "unconfigured";
  const financeConfigured = !!(env.FINNHUB_API_KEY || env.ALPHA_VANTAGE_API_KEY || env.FMP_API_KEY);
  const newsConfigured = !!env.NEWS_API_KEY;
  const llmForWeb = anyLlmConfigured(env);

  const providers = {
    aiChat,
    cloudflareWorkersAi: cfAi,
    openrouter: env.OPENROUTER_API_KEY ? "configured" : "unconfigured",
    groq: env.GROQ_API_KEY ? "configured" : "unconfigured",
    openai: env.OPENAI_API_KEY ? "configured" : "unconfigured",
    crypto: "configured",
    weather: "unconfigured",
    finance: financeConfigured ? "configured" : "unconfigured",
    news: newsConfigured ? "configured" : "unconfigured",
    webResearch: llmForWeb ? "configured" : "unconfigured",
  };

  return new Response(
    JSON.stringify({
      ok: true,
      releaseChannel: env.RELEASE_CHANNEL ?? "unknown",
      defaultModel: env.DEFAULT_MODEL ?? "",
      providers,
    }),
    { status: 200, headers: jsonHeaders },
  );
}

function extractBetaToken(request: Request, bodyToken?: string): string | undefined {
  const auth = request.headers.get("authorization");
  if (auth && auth.toLowerCase().startsWith("bearer ")) {
    return normalizeToken(auth.slice(7));
  }
  return normalizeToken(bodyToken);
}

function normalizeToken(value?: string): string | undefined {
  const trimmed = value?.trim();
  return trimmed ? trimmed : undefined;
}

async function handleResearchBody(bodyText: string, request: Request, env: GatewayEnv, maxBytes: number): Promise<Response> {
  let body: KyraResearchRequest;
  try {
    body = JSON.parse(bodyText) as KyraResearchRequest;
  } catch {
    return jsonResearch(
      {
        ok: false,
        errorCode: "BadJson",
        safeMessage: "Invalid realtime research request.",
      },
      400,
    );
  }

  const token = extractBetaToken(request, body.betaToken);
  const expectedToken = normalizeToken(env.BETA_GATEWAY_TOKEN);
  if (!expectedToken || !token || token !== expectedToken) {
    return jsonResearch(
      {
        ok: false,
        errorCode: "unauthorized",
        safeMessage: "Kyra beta gateway token is missing or invalid.",
      },
      401,
    );
  }

  const rateLimit = await evaluateRateLimit(request, env, token);
  if (!rateLimit.ok) {
    return jsonResearch(
      {
        ok: false,
        errorCode: "rate_limited",
        safeMessage: "Kyra beta API time is used up for today. Local/offline mode is still available.",
        retryAfterSeconds: rateLimit.retryAfterSeconds,
      },
      429,
    );
  }

  if (!body.consent?.gatewayResearch) {
    return jsonResearch(
      {
        ok: false,
        errorCode: "consent_required",
        safeMessage: "Realtime gateway research requires consent in the app.",
      },
      403,
    );
  }

  const prompt = sanitizeForProvider(body.prompt ?? "");
  if (!prompt) {
    return jsonResearch({ ok: false, errorCode: "EmptyPrompt", safeMessage: "Kyra needs a prompt to answer." }, 400);
  }

  const intent = (body.intent ?? "chat").toLowerCase();
  const ctx = body.context ?? {};
  const factsLine =
    ctx.knownLocalFacts && typeof ctx.knownLocalFacts === "object"
      ? sanitizeForProvider(
          Object.entries(ctx.knownLocalFacts)
            .map(([k, v]) => (v ? `${k}=${v}` : ""))
            .filter(Boolean)
            .join(","),
        ).slice(0, 400)
      : "";
  const ctxLine = [
    ctx.machineClass ? `machineClass=${sanitizeForProvider(ctx.machineClass)}` : "",
    ctx.healthScoreBand ? `health=${sanitizeForProvider(ctx.healthScoreBand)}` : "",
    ctx.usbState ? `usbState=${sanitizeForProvider(ctx.usbState)}` : "",
    ctx.privacyMode ? `privacyMode=${sanitizeForProvider(ctx.privacyMode)}` : "",
    ctx.manufacturer ? `mfg=${sanitizeForProvider(ctx.manufacturer)}` : "",
    ctx.modelFamily ? `model=${sanitizeForProvider(ctx.modelFamily)}` : "",
    ctx.partCategory ? `part=${sanitizeForProvider(ctx.partCategory)}` : "",
    factsLine ? `localBands=${factsLine}` : "",
  ]
    .filter(Boolean)
    .join("; ");

  const nowIso = new Date().toISOString();

  if (intent === "crypto") {
    const live = await fetchCryptoLive(prompt);
    if (live) {
      return jsonResearch({
        ok: true,
        answer: live.answer,
        tool: "crypto",
        provider: live.provider,
        freshnessUtc: nowIso,
        confidence: "high",
        metadata: { liveResearch: true, sanitizedContext: true },
      });
    }
    return jsonResearch(
      {
        ok: false,
        errorCode: "provider_unavailable",
        safeMessage:
          "I couldn’t load live BTC pricing right now. The crypto live tool may be unavailable or rate-limited. Try again in a minute or check provider settings.",
      },
      503,
    );
  }

  if (intent === "software_version" || (prompt.toLowerCase().includes("ventoy") && prompt.toLowerCase().includes("version"))) {
    const v = await fetchVentoyLatestVersion();
    if (v) {
      return jsonResearch({
        ok: true,
        answer: v,
        tool: "software_version",
        provider: "github",
        freshnessUtc: nowIso,
        confidence: "high",
        metadata: { liveResearch: true, sanitizedContext: true },
      });
    }
  }

  if (intent === "finance") {
    const financeConfigured = !!(env.FINNHUB_API_KEY || env.ALPHA_VANTAGE_API_KEY || env.FMP_API_KEY);
    return jsonResearch(
      {
        ok: false,
        errorCode: financeConfigured ? "provider_unavailable" : "not_configured",
        safeMessage: financeConfigured
          ? "Live finance data is configured on the gateway, but the finance research tool could not verify this request right now. I will not invent market data."
          : "Live finance data is not configured on the gateway yet. I will not invent current stock market changes.",
      },
      financeConfigured ? 503 : 501,
    );
  }

  if (!anyLlmConfigured(env)) {
    return jsonResearch({
      ok: false,
      errorCode: "not_configured",
      safeMessage: "The realtime gateway has no AI provider configured server-side.",
    }, 503);
  }

  const researchSystem =
    intent === "hardware_part_lookup"
      ? `You are Kyra (ForgerEMS) answering hardware part / upgrade shopping questions from realtime research.
Rules:
- Never invent exact OEM part numbers, sticker SKUs, or “guaranteed compatible” claims without labeling them as unverified candidates.
- If you cite prices, ranges, or “cheapest”, only do so when you are summarizing plausible live marketplace patterns in generic terms — never fabricate specific listings, part IDs, or stock.
- Prefer official/service-manual/OEM compatibility language for fit; treat third-party sellers as availability hints only.
- Use only the sanitized machine hints in the operator context line (manufacturer/model family/part category/local bands). Do not ask for service tags, serials, or private paths.
- Include a short “confirm before buy” reminder (manual, battery label, or OEM compatibility matrix).
- Keep answers concise and technician-practical.`
      : `You are Kyra (ForgerEMS). Realtime research mode.
Rules:
- Never invent live market prices, sports scores, exact CVE details, or version numbers unless you state they are illustrative.
- If you cannot verify current facts, say the live tool or gateway could not verify and suggest checking an official source.
- Do not ask for secrets. Keep answers concise.`;

  const userContent = `Sanitized operator context: ${ctxLine || "none"}

User question:
${prompt}`.slice(0, maxBytes);

  const routingCtx = ctxLine;
  const pseudoBody: KyraGatewayRequest = {
    personality: "bubbly-tech",
    maxTokens: 900,
    temperature: 0.35,
  };

  const llm = await runLlmInferenceChain(env, pseudoBody, userContent, researchSystem, intent, prompt, routingCtx);

  if (llm.ok) {
    const text = llm.message;
    if (intent === "crypto" || intent === "finance") {
      if (/\bknowledge cutoff\b|as of my last update|don't have real-time|do not have real-time/i.test(text)) {
        return jsonResearch(
          {
            ok: false,
            errorCode: "stale_wording_rejected",
            safeMessage:
              "I couldn’t return verified live market data from the gateway. Try again shortly or check operator configuration.",
          },
          503,
        );
      }
    }
    return jsonResearch({
      ok: true,
      answer: text,
      tool: "llm",
      provider: llm.providerUsed,
      freshnessUtc: nowIso,
      confidence: "medium",
      metadata: { liveResearch: true, sanitizedContext: true },
    });
  }

  return jsonResearch(
    {
      ok: false,
      errorCode: "provider_unavailable",
      safeMessage:
        "I couldn’t complete realtime research right now. The gateway provider may be unavailable. Local Kyra is still available.",
    },
    503,
  );
}

async function fetchCryptoLive(prompt: string): Promise<{ answer: string; provider: string } | null> {
  const l = prompt.toLowerCase();
  const ids: string[] = [];
  if (l.includes("btc") || l.includes("bitcoin")) {
    ids.push("bitcoin");
  }
  if (l.includes("eth") || l.includes("ethereum")) {
    ids.push("ethereum");
  }
  if (l.includes("sol") || l.includes("solana")) {
    ids.push("solana");
  }
  if (ids.length === 0) {
    ids.push("bitcoin");
  }

  const url = `https://api.coingecko.com/api/v3/simple/price?ids=${encodeURIComponent(ids.join(","))}&vs_currencies=usd&include_24hr_change=true`;
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 12000);
  try {
    const res = await fetch(url, { signal: controller.signal, headers: { accept: "application/json" } });
    if (!res.ok) {
      return null;
    }
    const data = (await res.json()) as Record<string, { usd?: number; usd_24h_change?: number }>;
    const lines: string[] = [];
    for (const id of ids) {
      const row = data[id];
      if (!row?.usd) {
        continue;
      }
      const sym = id === "bitcoin" ? "BTC" : id === "ethereum" ? "ETH" : id === "solana" ? "SOL" : id.toUpperCase();
      const chg = row.usd_24h_change;
      const chgTxt = typeof chg === "number" ? ` (24h ${chg >= 0 ? "+" : ""}${chg.toFixed(2)}%)` : "";
      lines.push(`${sym}: about $${row.usd.toLocaleString("en-US", { maximumFractionDigits: 2 })} USD${chgTxt} via CoinGecko (informational, not financial advice).`);
    }
    if (lines.length === 0) {
      return null;
    }
    return { answer: lines.join("\n"), provider: "coingecko" };
  } catch {
    return null;
  } finally {
    clearTimeout(timeout);
  }
}

async function fetchVentoyLatestVersion(): Promise<string | null> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 12000);
  try {
    const res = await fetch("https://api.github.com/repos/ventoy/Ventoy/releases/latest", {
      signal: controller.signal,
      headers: {
        accept: "application/vnd.github+json",
        "user-agent": "ForgerEMS-Kyra-Gateway",
      },
    });
    if (!res.ok) {
      return null;
    }
    const data = (await res.json()) as { tag_name?: string; html_url?: string };
    const tag = data.tag_name ?? "unknown";
    const link = data.html_url ?? "https://github.com/ventoy/Ventoy/releases";
    return `Latest Ventoy release on GitHub is tagged ${tag}. Confirm on ventoy.net or the official repo before downloading: ${link}`;
  } catch {
    return null;
  } finally {
    clearTimeout(timeout);
  }
}

async function handleLegacyChatBody(bodyText: string, request: Request, env: GatewayEnv, maxBytes: number): Promise<Response> {
  let body: KyraGatewayRequest;
  try {
    body = JSON.parse(bodyText) as KyraGatewayRequest;
  } catch {
    return jsonLegacy({ ok: false, errorCode: "BadJson", message: "Invalid gateway request." }, 400);
  }

  const token = extractBetaToken(request, body.betaToken);
  const expectedToken = normalizeToken(env.BETA_GATEWAY_TOKEN);
  if (!expectedToken || !token || token !== expectedToken) {
    return jsonLegacy({ ok: false, errorCode: "Unauthorized", message: "Kyra beta gateway token is missing or invalid." }, 401);
  }

  const rateLimit = await evaluateRateLimit(request, env, token);
  if (!rateLimit.ok) {
    return jsonLegacy(
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

  const prompt = extractChatUserText(body, sanitizeForProvider);
  if (!prompt) {
    return jsonLegacy({ ok: false, errorCode: "EmptyPrompt", message: "Kyra needs a prompt to answer." }, 400);
  }

  const context = body.machineContext?.summary
    ? `\n\nSanitized machine context:\n${sanitizeForProvider(body.machineContext.summary)}`
    : "";
  const memory = body.memorySummary ? `\n\nSafe memory summary:\n${sanitizeForProvider(body.memorySummary)}` : "";
  const userContent = `${prompt}${context}${memory}`.slice(0, maxBytes);

  if (!anyLlmConfigured(env)) {
    return jsonLegacy({ ok: false, errorCode: "NoProviderConfigured", message: "Kyra gateway has no server-side provider configured." }, 503);
  }

  const systemPrompt = `You are Kyra, the ForgerEMS beta assistant. Be concise, useful, and do not ask for secrets. Personality: ${body.personality ?? "bubbly-tech"}.`;
  const intent = (body.intent ?? "chat").toLowerCase();
  const routingCtx = body.machineContext?.summary ? sanitizeForProvider(body.machineContext.summary).slice(0, 2000) : "";

  const llm = await runLlmInferenceChain(env, body, userContent, systemPrompt, intent, prompt, routingCtx);

  if (llm.ok) {
    return jsonLegacy({
      ok: true,
      providerUsed: llm.providerUsed,
      modelUsed: llm.modelUsed,
      message: llm.message,
      toolResults: [],
      fallbackUsed: llm.fallbackUsed,
      diagnosticNote: "gateway response",
    });
  }

  return jsonLegacy({
    ok: false,
    errorCode: "GatewayProviderFailure",
    message: "Kyra gateway providers are unavailable. Local/offline mode is still available.",
    diagnosticNote: llm.errorCode,
  }, 503);
}

type LlmChainOk = {
  ok: true;
  message: string;
  providerUsed: string;
  modelUsed: string;
  fallbackUsed: boolean;
};

type LlmChainFail = { ok: false; errorCode: string };

export async function runLlmInferenceChain(
  env: GatewayEnv,
  body: KyraGatewayRequest,
  userContent: string,
  systemPrompt: string,
  intent: string,
  routingPrompt: string,
  routingContext: string,
): Promise<LlmChainOk | LlmChainFail> {
  const timeoutMs = Number(env.PROVIDER_TIMEOUT_MS ?? "45000");
  const { selectedModel, route } = selectGatewayModel(intent, routingPrompt, routingContext, env);
  const cfModels = workersAiAvailable(env) ? buildCfInferenceSequence(selectedModel, route, env) : [];
  const externals = buildExternalProviders(env);

  let attempt = 0;
  let firstRoute = true;

  for (const modelId of cfModels) {
    const result = await callCloudflareWorkersAi(env, modelId, systemPrompt, userContent, body, timeoutMs);
    if (result.ok) {
      return {
        ok: true,
        message: result.message,
        providerUsed: "cloudflare-workers-ai",
        modelUsed: modelId,
        fallbackUsed: !firstRoute,
      };
    }
    firstRoute = false;
    attempt++;
  }

  for (let i = 0; i < externals.length; i++) {
    const provider = externals[i];
    const result = await callOpenAiCompatible(provider, body, userContent, timeoutMs, systemPrompt);
    if (result.ok) {
      return {
        ok: true,
        message: result.message,
        providerUsed: provider.name,
        modelUsed: provider.model,
        fallbackUsed: attempt > 0 || i > 0,
      };
    }
    attempt++;
  }

  return { ok: false, errorCode: "AllProvidersFailed" };
}

function extractWorkersAiText(result: unknown): string {
  if (typeof result === "string") {
    return result.trim();
  }
  if (!result || typeof result !== "object") {
    return "";
  }
  const r = result as Record<string, unknown>;
  if (typeof r.response === "string" && r.response.trim()) {
    return r.response.trim();
  }
  const nested = r.result;
  if (nested && typeof nested === "object") {
    const nr = nested as Record<string, unknown>;
    if (typeof nr.response === "string" && nr.response.trim()) {
      return nr.response.trim();
    }
  }
  if (typeof r.output_text === "string" && r.output_text.trim()) {
    return r.output_text.trim();
  }
  return "";
}

async function callCloudflareWorkersAi(
  env: GatewayEnv,
  modelId: string,
  systemPrompt: string,
  userContent: string,
  body: KyraGatewayRequest,
  providerTimeoutMs: number,
): Promise<{ ok: true; message: string } | { ok: false; errorCode: string }> {
  if (!modelId.startsWith("@cf/")) {
    return { ok: false, errorCode: "NotCloudflareModel" };
  }
  if (!workersAiAvailable(env)) {
    return { ok: false, errorCode: "WorkersAiUnbound" };
  }

  const ai = env.AI;
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), clamp(providerTimeoutMs, 1500, 90000));
  const maxTok = Math.min(Math.max(body.maxTokens ?? 1000, 128), 2048);
  const temp = Math.min(Math.max(body.temperature ?? 0.5, 0), 1);

  try {
    const messagesPayload: Record<string, unknown> = {
      messages: [
        { role: "system", content: systemPrompt },
        { role: "user", content: userContent },
      ],
      max_tokens: maxTok,
      temperature: temp,
    };

    let result: unknown;
    try {
      result = await ai.run(modelId, messagesPayload, { signal: controller.signal });
    } catch {
      result = null;
    }

    let text = extractWorkersAiText(result);
    if (!text) {
      const altPayload: Record<string, unknown> = {
        instructions: systemPrompt,
        input: userContent,
        max_tokens: maxTok,
        temperature: temp,
      };
      try {
        result = await ai.run(modelId, altPayload, { signal: controller.signal });
      } catch {
        result = null;
      }
      text = extractWorkersAiText(result);
    }

    return text ? { ok: true, message: text } : { ok: false, errorCode: "EmptyWorkersAiResponse" };
  } catch {
    return { ok: false, errorCode: "WorkersAiFailure" };
  } finally {
    clearTimeout(timeout);
  }
}

async function callOpenAiCompatible(
  provider: { baseUrl: string; key: string; model: string },
  body: KyraGatewayRequest,
  userContent: string,
  providerTimeoutMs: number,
  systemPrompt: string,
): Promise<{ ok: true; message: string } | { ok: false; errorCode: string }> {
  if (provider.model.startsWith("@cf/")) {
    return { ok: false, errorCode: "InvalidOpenAiModel" };
  }
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort("provider-timeout"), clamp(providerTimeoutMs, 1500, 90000));
  let response: Response;
  try {
    response = await fetch(`${provider.baseUrl}/chat/completions`, {
      method: "POST",
      headers: {
        authorization: `Bearer ${provider.key}`,
        "content-type": "application/json",
      },
      body: JSON.stringify({
        model: provider.model,
        messages: [
          {
            role: "system",
            content: systemPrompt,
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

function jsonLegacy(payload: KyraGatewayResponse, status = 200): Response {
  return new Response(JSON.stringify(payload), { status, headers: jsonHeaders });
}

function jsonResearch(payload: KyraResearchResponse, status = 200): Response {
  return new Response(JSON.stringify(payload), { status, headers: jsonHeaders });
}

function sanitizeForProvider(value: string): string {
  return value
    .replace(/(api[_-]?key|token|secret|password)\s*[:=]\s*["']?[^"'\s;]+/gi, "[redacted]")
    .replace(/\bsk-[A-Za-z0-9_-]{12,}\b/gi, "[redacted]")
    .replace(/\bgsk_[A-Za-z0-9_-]{12,}\b/gi, "[redacted]")
    .replace(/\bAIza[A-Za-z0-9_-]{16,}\b/g, "[redacted]")
    .replace(/\b(ghp|gho|github_pat)_[A-Za-z0-9_]{20,}\b/gi, "[redacted]")
    .replace(/\bBearer\s+[A-Za-z0-9._~+/=-]{12,}\b/gi, "Bearer [redacted]")
    .replace(/\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b/gi, "[email redacted]")
    .replace(/\b(?:(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\b/g, "[ip redacted]")
    .replace(/\b[A-Z0-9]{5}(?:-[A-Z0-9]{5}){4}\b/gi, "[product key redacted]")
    .replace(/[A-Za-z]:\\[^\s\r\n]+/g, "[private path redacted]")
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
  env: GatewayEnv,
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
