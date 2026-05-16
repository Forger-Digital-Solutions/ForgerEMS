import type { GatewayEnv } from "./gateway-env";

export type GatewayRouteKind = "coding" | "heavy" | "normal";

export type KyraGatewayRequestLike = {
  userMessage?: string;
  message?: string;
  prompt?: string;
  input?: string;
  intent?: string;
  machineContext?: Record<string, string> & { summary?: string };
};

const codingHints =
  /\b(code|coding|debug|refactor|typescript|javascript|python|c#|\.net|java|rust|golang|react|vue|angular|sql|stack trace|exception|compiler|lint|unit test|api route|function\s|class\s|implement|pull request|git diff)\b/i;

const heavyHints =
  /\b(architecture|system design|tradeoff|trade-off|deep dive|prove|formal|planning|complex debugging|multi-?step reasoning|evaluate approaches|root cause analysis|risk analysis|threat model|performance plan|capacity planning)\b/i;

/** First non-blank among legacy desktop + standalone-friendly fields. */
export function extractChatUserText(body: KyraGatewayRequestLike, sanitize: (s: string) => string): string {
  const fields = [body.userMessage, body.message, body.prompt, body.input];
  for (const f of fields) {
    if (typeof f !== "string") {
      continue;
    }
    const s = sanitize(f).trim();
    if (s) {
      return s;
    }
  }
  return "";
}

export function selectGatewayModel(
  intent: string | undefined,
  prompt: string,
  context: string,
  env: Pick<GatewayEnv, "DEFAULT_MODEL" | "CODING_MODEL" | "HEAVY_REASONING_MODEL">,
): { selectedModel: string; route: GatewayRouteKind } {
  const intentLower = (intent ?? "").toLowerCase();
  const haystack = `${intentLower}\n${prompt}\n${context}`;

  const codingModel = env.CODING_MODEL ?? "@cf/qwen/qwen2.5-coder-32b-instruct";
  const heavyModel = env.HEAVY_REASONING_MODEL ?? "@cf/openai/gpt-oss-120b";
  const defaultModel = env.DEFAULT_MODEL ?? "@cf/openai/gpt-oss-20b";
  const normalCfDefault = defaultModel.startsWith("@cf/") ? defaultModel : "@cf/openai/gpt-oss-20b";

  if (intentLower === "code" || intentLower === "debug" || intentLower === "refactor" || codingHints.test(haystack)) {
    return { selectedModel: codingModel, route: "coding" };
  }

  if (intentLower === "plan" || intentLower === "architecture" || heavyHints.test(haystack)) {
    return { selectedModel: heavyModel, route: "heavy" };
  }

  return { selectedModel: normalCfDefault, route: "normal" };
}

/** Cloudflare Workers AI try order: primary → general fallback → reasoning (heavy only). */
export function buildCfInferenceSequence(
  selectedModel: string,
  route: GatewayRouteKind,
  env: Pick<GatewayEnv, "GENERAL_FALLBACK_MODEL" | "REASONING_FALLBACK_MODEL">,
): string[] {
  const general = env.GENERAL_FALLBACK_MODEL ?? "@cf/meta/llama-3.3-70b-instruct-fp8-fast";
  const reasoning = env.REASONING_FALLBACK_MODEL ?? "@cf/deepseek-ai/deepseek-r1-distill-qwen-32b";
  const out: string[] = [];
  const add = (m: string) => {
    if (m.startsWith("@cf/") && !out.includes(m)) {
      out.push(m);
    }
  };
  add(selectedModel);
  add(general);
  if (route === "heavy") {
    add(reasoning);
  }
  return out;
}
