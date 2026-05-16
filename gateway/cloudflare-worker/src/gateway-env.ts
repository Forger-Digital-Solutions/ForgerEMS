/** Workers AI binding (see wrangler `[ai]`). */
export type WorkersAiBinding = {
  run(model: string, inputs: unknown, options?: { signal?: AbortSignal }): Promise<unknown>;
};

export interface GatewayEnv {
  BETA_GATEWAY_TOKEN: string;
  OPENAI_API_KEY?: string;
  OPENROUTER_API_KEY?: string;
  GROQ_API_KEY?: string;
  NEWS_API_KEY?: string;
  FINNHUB_API_KEY?: string;
  ALPHA_VANTAGE_API_KEY?: string;
  FMP_API_KEY?: string;
  RELEASE_CHANNEL?: string;
  DEFAULT_MODEL?: string;
  HEAVY_REASONING_MODEL?: string;
  CODING_MODEL?: string;
  GENERAL_FALLBACK_MODEL?: string;
  REASONING_FALLBACK_MODEL?: string;
  MAX_REQUEST_BYTES?: string;
  PROVIDER_TIMEOUT_MS?: string;
  BETA_DAILY_TOKEN_LIMIT?: string;
  BETA_DAILY_IP_LIMIT?: string;
  RATE_LIMITS_ENABLED?: string;
  RATE_LIMIT_KV?: KVNamespace;
  /** Present when wrangler `[ai]` binding is configured. */
  AI?: WorkersAiBinding;
}
