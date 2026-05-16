import { describe, expect, it } from "vitest";
import { buildCfInferenceSequence, extractChatUserText, selectGatewayModel } from "../src/kyra-gateway-routing";
import type { GatewayEnv } from "../src/gateway-env";

function sanitize(s: string): string {
  return s;
}

describe("extractChatUserText", () => {
  it("accepts message", () => {
    expect(extractChatUserText({ message: "hello" }, sanitize)).toBe("hello");
  });
  it("accepts prompt", () => {
    expect(extractChatUserText({ prompt: "hello" }, sanitize)).toBe("hello");
  });
  it("accepts input", () => {
    expect(extractChatUserText({ input: "hello" }, sanitize)).toBe("hello");
  });
  it("prefers userMessage over message", () => {
    expect(extractChatUserText({ userMessage: "a", message: "b" }, sanitize)).toBe("a");
  });
  it("returns empty when all blank", () => {
    expect(extractChatUserText({ message: "   ", prompt: "", input: undefined }, sanitize)).toBe("");
  });
});

describe("selectGatewayModel", () => {
  const base: Pick<GatewayEnv, "DEFAULT_MODEL" | "CODING_MODEL" | "HEAVY_REASONING_MODEL"> = {
    DEFAULT_MODEL: "@cf/openai/gpt-oss-20b",
    CODING_MODEL: "@cf/qwen/qwen2.5-coder-32b-instruct",
    HEAVY_REASONING_MODEL: "@cf/openai/gpt-oss-120b",
  };

  it("selects CODING_MODEL for coding-style prompts", () => {
    const r = selectGatewayModel("chat", "fix this TypeScript compile error", "", base);
    expect(r.route).toBe("coding");
    expect(r.selectedModel).toBe("@cf/qwen/qwen2.5-coder-32b-instruct");
  });

  it("selects HEAVY_REASONING_MODEL for architecture-style prompts", () => {
    const r = selectGatewayModel("plan", "compare tradeoffs for system design", "", base);
    expect(r.route).toBe("heavy");
    expect(r.selectedModel).toBe("@cf/openai/gpt-oss-120b");
  });

  it("selects HEAVY_REASONING_MODEL for deep reasoning phrasing", () => {
    const r = selectGatewayModel("chat", "Need deep reasoning for complex debugging steps", "", base);
    expect(r.route).toBe("heavy");
    expect(r.selectedModel).toBe("@cf/openai/gpt-oss-120b");
  });

  it("uses DEFAULT_MODEL for normal chat when @cf/", () => {
    const r = selectGatewayModel("chat", "hello there", "", base);
    expect(r.route).toBe("normal");
    expect(r.selectedModel).toBe("@cf/openai/gpt-oss-20b");
  });

  it("uses gpt-oss-20b when DEFAULT_MODEL is non-@cf", () => {
    const r = selectGatewayModel("chat", "hello", "", { ...base, DEFAULT_MODEL: "gpt-4o-mini" });
    expect(r.selectedModel).toBe("@cf/openai/gpt-oss-20b");
  });
});

describe("buildCfInferenceSequence", () => {
  const fb: Pick<GatewayEnv, "GENERAL_FALLBACK_MODEL" | "REASONING_FALLBACK_MODEL"> = {
    GENERAL_FALLBACK_MODEL: "@cf/meta/llama-3.3-70b-instruct-fp8-fast",
    REASONING_FALLBACK_MODEL: "@cf/deepseek-ai/deepseek-r1-distill-qwen-32b",
  };

  it("orders primary then general; adds reasoning only for heavy", () => {
    const normal = buildCfInferenceSequence("@cf/openai/gpt-oss-20b", "normal", fb);
    expect(normal).toEqual(["@cf/openai/gpt-oss-20b", "@cf/meta/llama-3.3-70b-instruct-fp8-fast"]);
    const heavy = buildCfInferenceSequence("@cf/openai/gpt-oss-120b", "heavy", fb);
    expect(heavy).toEqual([
      "@cf/openai/gpt-oss-120b",
      "@cf/meta/llama-3.3-70b-instruct-fp8-fast",
      "@cf/deepseek-ai/deepseek-r1-distill-qwen-32b",
    ]);
  });
});
