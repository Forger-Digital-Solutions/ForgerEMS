namespace VentoyToolkitSetup.Wpf.Infrastructure;

/// <summary>First-run Kyra copy — no secrets or host-specific paths.</summary>
public static class KyraOnboardingCopy
{
    public const string InitialWelcomeMessage =
        "Hi — I’m Kyra. This build is **beta** software: double-check USB targets before destructive steps.\n\n" +
        "Next steps: run **System Intelligence** (sidebar), open **USB Builder** / **USB Intelligence** (benchmarks & port mapping) if you’re prepping a stick, and run **USB Benchmark** once a safe removable USB is selected. " +
        "Offline answers use local rules — no API keys required. Optional online providers are **off** until you enable them in **Kyra Advanced** (see repo `docs/KYRA_PROVIDER_ENVIRONMENT_SETUP.md` for Windows env vars and LM Studio / Ollama / OpenAI-compatible setup).\n\n" +
        "Try asking:\n" +
        "• “How do I map USB ports?”\n" +
        "• “Which port should I use for this stick?”\n" +
        "• “Run a quick check on this PC” (after a System Scan)\n" +
        "• “What’s missing before beta testing?”\n\n" +
        "Slash commands still work — e.g. /help, /diagnose, /usb, /resale, /fixcode.";
}
