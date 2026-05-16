namespace VentoyToolkitSetup.Wpf.Services.Kyra;

internal static class KyraMathCopilotResponseBuilder
{
    public static bool TryBuildCopilotResponse(string? prompt, out CopilotResponse response)
    {
        response = new CopilotResponse();
        if (!KyraSimpleMathEvaluator.TryEvaluate(prompt, out var text, out _))
        {
            return false;
        }

        response = new CopilotResponse
        {
            Text = text.Trim(),
            UsedOnlineData = false,
            OnlineStatus = "Kyra local calculator (deterministic).",
            ProviderType = CopilotProviderType.LocalOffline,
            ProviderNotes = ["Kyra routing: local calculator -> success"],
            ResponseSource = KyraResponseSource.LocalKyra,
            SourceLabel = "Local tool • Calculator",
            GroundedInSystemIntelligence = false,
            ActionSuggestions = []
        };
        return true;
    }
}
