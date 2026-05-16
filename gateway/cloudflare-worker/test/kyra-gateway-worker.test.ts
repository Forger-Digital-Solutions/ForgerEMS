/// <reference types="@cloudflare/workers-types" />

import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it, vi } from "vitest";
import type { GatewayEnv } from "../src/gateway-env";
import worker, { runLlmInferenceChain } from "../src/index";

const __dirname = dirname(fileURLToPath(import.meta.url));

function jsonRequest(path: string, body: unknown, token: string): Request {
  return new Request(`https://gw.example${path}`, {
    method: "POST",
    headers: {
      authorization: `Bearer ${token}`,
      "content-type": "application/json",
    },
    body: JSON.stringify(body),
  });
}

function statusRequest(token: string): Request {
  return new Request(`https://gw.example/v1/kyra/status`, {
    method: "GET",
    headers: { authorization: `Bearer ${token}` },
  });
}

describe("wrangler.toml", () => {
  it("does not ship raw provider-style secrets", () => {
    const p = join(__dirname, "..", "wrangler.toml");
    const raw = readFileSync(p, "utf-8");
    expect(raw).not.toMatch(/\bsk-[a-zA-Z0-9]{10,}\b/);
    expect(raw).not.toMatch(/\bgsk_[a-zA-Z0-9]{10,}\b/);
    expect(raw).not.toMatch(/OPENAI_API_KEY\s*=\s*"/);
  });
});

describe("POST /v1/kyra/chat", () => {
  const token = "test-beta-token";

  function envWithAi(runImpl: (model: string) => Promise<unknown>): GatewayEnv {
    return {
      BETA_GATEWAY_TOKEN: token,
      DEFAULT_MODEL: "@cf/openai/gpt-oss-20b",
      GENERAL_FALLBACK_MODEL: "@cf/meta/llama-3.3-70b-instruct-fp8-fast",
      REASONING_FALLBACK_MODEL: "@cf/deepseek-ai/deepseek-r1-distill-qwen-32b",
      CODING_MODEL: "@cf/qwen/qwen2.5-coder-32b-instruct",
      HEAVY_REASONING_MODEL: "@cf/openai/gpt-oss-120b",
      AI: { run: vi.fn((m: string) => runImpl(m)) },
    };
  }

  it("returns EmptyPrompt only when message, prompt, input, and userMessage are blank", async () => {
    const env = envWithAi(async () => ({ response: "x" }));
    const res = await worker.fetch(jsonRequest("/v1/kyra/chat", { intent: "chat" }, token), env, {} as ExecutionContext);
    expect(res.status).toBe(400);
    const j = (await res.json()) as { errorCode?: string };
    expect(j.errorCode).toBe("EmptyPrompt");
  });

  it("accepts message", async () => {
    const env = envWithAi(async () => ({ response: "online" }));
    const res = await worker.fetch(
      jsonRequest("/v1/kyra/chat", { message: "Say Kyra gateway is online.", intent: "chat" }, token),
      env,
      {} as ExecutionContext,
    );
    expect(res.status).toBe(200);
    const j = (await res.json()) as { ok: boolean; providerUsed?: string; modelUsed?: string };
    expect(j.ok).toBe(true);
    expect(j.providerUsed).toBe("cloudflare-workers-ai");
    expect(j.modelUsed).toBe("@cf/openai/gpt-oss-20b");
  });

  it("accepts prompt", async () => {
    const env = envWithAi(async () => ({ response: "ok" }));
    const res = await worker.fetch(jsonRequest("/v1/kyra/chat", { prompt: "hello", intent: "chat" }, token), env, {} as ExecutionContext);
    expect(res.status).toBe(200);
  });

  it("accepts input", async () => {
    const env = envWithAi(async () => ({ response: "ok" }));
    const res = await worker.fetch(jsonRequest("/v1/kyra/chat", { input: "hello", intent: "chat" }, token), env, {} as ExecutionContext);
    expect(res.status).toBe(200);
  });

  it("does not echo beta token or API keys in JSON responses", async () => {
    const secret = "sk-test1234567890abcdefghijklmnop";
    const env: GatewayEnv = {
      ...envWithAi(async () => ({ response: "safe reply without echo" })),
      OPENAI_API_KEY: secret,
    };
    const res = await worker.fetch(
      jsonRequest(
        "/v1/kyra/chat",
        { message: `ignore ${secret}`, intent: "chat" },
        token,
      ),
      env,
      {} as ExecutionContext,
    );
    const text = await res.text();
    expect(text).not.toContain(secret);
    expect(text).not.toContain(token);
  });
});

describe("runLlmInferenceChain fallback order", () => {
  it("uses Workers AI before OpenRouter when DEFAULT_MODEL is @cf/", async () => {
    const calls: string[] = [];
    const env: GatewayEnv = {
      BETA_GATEWAY_TOKEN: "t",
      DEFAULT_MODEL: "@cf/openai/gpt-oss-20b",
      GENERAL_FALLBACK_MODEL: "@cf/meta/llama-3.3-70b-instruct-fp8-fast",
      REASONING_FALLBACK_MODEL: "@cf/deepseek-ai/deepseek-r1-distill-qwen-32b",
      CODING_MODEL: "@cf/qwen/qwen2.5-coder-32b-instruct",
      HEAVY_REASONING_MODEL: "@cf/openai/gpt-oss-120b",
      OPENROUTER_API_KEY: "dummy-openrouter",
      AI: {
        run: vi.fn(async (model: string) => {
          calls.push(model);
          if (model === "@cf/openai/gpt-oss-20b") {
            return { response: "cf ok" };
          }
          return {};
        }),
      },
    };

    const body = { maxTokens: 256, temperature: 0.5 };
    const r = await runLlmInferenceChain(
      env,
      body,
      "hello",
      "system",
      "chat",
      "hello",
      "",
    );
    expect(r.ok && r.message).toBeTruthy();
    if (r.ok) {
      expect(r.providerUsed).toBe("cloudflare-workers-ai");
      expect(calls[0]).toBe("@cf/openai/gpt-oss-20b");
    }
  });

  it("falls through Cloudflare sequence then external providers", async () => {
    const models: string[] = [];
    const origFetch = globalThis.fetch;
    globalThis.fetch = vi.fn(async (input: RequestInfo | URL) => {
      const u = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
      if (u.includes("openrouter.ai")) {
        return new Response(
          JSON.stringify({ choices: [{ message: { content: "from openrouter" } }] }),
          { status: 200, headers: { "content-type": "application/json" } },
        );
      }
      return new Response("{}", { status: 500 });
    }) as typeof fetch;

    const env: GatewayEnv = {
      BETA_GATEWAY_TOKEN: "t",
      DEFAULT_MODEL: "@cf/openai/gpt-oss-20b",
      GENERAL_FALLBACK_MODEL: "@cf/meta/llama-3.3-70b-instruct-fp8-fast",
      REASONING_FALLBACK_MODEL: "@cf/deepseek-ai/deepseek-r1-distill-qwen-32b",
      CODING_MODEL: "@cf/qwen/qwen2.5-coder-32b-instruct",
      HEAVY_REASONING_MODEL: "@cf/openai/gpt-oss-120b",
      OPENROUTER_API_KEY: "dummy",
      AI: {
        run: vi.fn(async (model: string) => {
          models.push(model);
          return {};
        }),
      },
    };

    try {
      const r = await runLlmInferenceChain(env, { maxTokens: 256, temperature: 0.5 }, "hi", "sys", "chat", "hi", "");
      expect(r.ok).toBe(true);
      if (r.ok) {
        expect(r.providerUsed).toBe("openrouter");
        expect(r.fallbackUsed).toBe(true);
        expect(models.length).toBeGreaterThanOrEqual(2);
      }
    } finally {
      globalThis.fetch = origFetch;
    }
  });

  it("selects coder model for refactor prompts", async () => {
    const models: string[] = [];
    const env: GatewayEnv = {
      BETA_GATEWAY_TOKEN: "t",
      DEFAULT_MODEL: "@cf/openai/gpt-oss-20b",
      GENERAL_FALLBACK_MODEL: "@cf/meta/llama-3.3-70b-instruct-fp8-fast",
      REASONING_FALLBACK_MODEL: "@cf/deepseek-ai/deepseek-r1-distill-qwen-32b",
      CODING_MODEL: "@cf/qwen/qwen2.5-coder-32b-instruct",
      HEAVY_REASONING_MODEL: "@cf/openai/gpt-oss-120b",
      AI: {
        run: vi.fn(async (model: string) => {
          models.push(model);
          return { response: "fixed" };
        }),
      },
    };
    const r = await runLlmInferenceChain(
      env,
      { maxTokens: 256, temperature: 0.5 },
      "refactor this function",
      "sys",
      "chat",
      "refactor this function",
      "",
    );
    expect(r.ok).toBe(true);
    if (r.ok) {
      expect(models[0]).toBe("@cf/qwen/qwen2.5-coder-32b-instruct");
    }
  });
});

describe("GET /v1/kyra/status", () => {
  it("returns extended safe metadata", async () => {
    const token = "kyra-status-test-token-7f3a9c";
    const env: GatewayEnv = {
      BETA_GATEWAY_TOKEN: token,
      RELEASE_CHANNEL: "preview",
      DEFAULT_MODEL: "@cf/openai/gpt-oss-20b",
      AI: { run: vi.fn() },
    };
    const res = await worker.fetch(statusRequest(token), env, {} as ExecutionContext);
    expect(res.status).toBe(200);
    const j = (await res.json()) as {
      ok: boolean;
      releaseChannel?: string;
      defaultModel?: string;
      providers?: Record<string, string>;
    };
    expect(j.ok).toBe(true);
    expect(j.releaseChannel).toBe("preview");
    expect(j.defaultModel).toContain("@cf/");
    expect(j.providers?.cloudflareWorkersAi).toBe("configured");
    expect(j.providers?.openai).toBe("unconfigured");
    const raw = JSON.stringify(j);
    expect(raw).not.toContain(token);
  });
});
