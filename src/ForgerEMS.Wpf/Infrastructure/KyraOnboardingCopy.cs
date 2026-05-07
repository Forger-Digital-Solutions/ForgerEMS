namespace VentoyToolkitSetup.Wpf.Infrastructure;

using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Services;

/// <summary>First-run Kyra copy — no secrets or host-specific paths.</summary>
public static class KyraOnboardingCopy
{
    public static string InitialWelcomeMessage => BuildInitialWelcomeMessage(new CopilotSettings());

    public static string BuildInitialWelcomeMessage(CopilotSettings settings)
    {
        var gatewayEnabled =
            settings.KyraRealtimeGatewayEnabled &&
            settings.KyraRealtimeGatewayResearchEnabled &&
            (settings.KyraRealtimeGatewayResearchConsent || !ForgerEmsEnvironmentConfiguration.KyraGatewayRequireConsent);
        var onlineLine = gatewayEnabled
            ? "Realtime Gateway is enabled. Live tools may be used for current-data questions when the gateway or Kyra Advanced live APIs are available."
            : "Offline answers work with no API keys. Optional online paths stay off until enabled in Kyra Advanced settings.";

        return
            "Hi, I’m Kyra. I can help with this device, USB prep, diagnostics, and quick troubleshooting.\n\n" +
            "Quick start: run System Intelligence first, then open USB Builder / USB Intelligence if you are prepping a stick, and ask me things like device-fit, warning explanations, or next-step repair plans. " +
            onlineLine + "\n\n" +
            "Try asking:\n" +
            "• “How do I map USB ports?”\n" +
            "• “Which port should I use for this stick?”\n" +
            "• “Run a quick check on this PC” (after a System Scan)\n" +
            "• “What’s missing before beta testing?”\n\n" +
            "Beta safety note: double-check USB targets before destructive steps.\n\n" +
            "Slash commands still work — e.g. /help, /diagnose, /usb, /resale, /fixcode.";
    }
}
