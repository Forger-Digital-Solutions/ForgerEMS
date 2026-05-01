using System.Linq;
using System.Net.Http;
using System.Text;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>Runs API-backed Kyra live tools for slash commands (no LLM required).</summary>
public static class KyraLiveSlashCoordinator
{
    public static bool IsLiveDataSlash(KyraSlashCommand? cmd) =>
        cmd?.Name is "weather" or "news" or "stocks" or "crypto" or "sports";

    public static async Task<KyraSlashHandleResult> ExecuteLiveAsync(
        KyraSlashCommandParseResult parse,
        CopilotSettings settings,
        KyraToolHostFacts facts,
        CancellationToken cancellationToken,
        HttpMessageHandler? testHttpHandler = null)
    {
        var cmd = parse.MatchedCommand!;
        var intent = cmd.HandlerIntent;
        var registry = new KyraToolRegistry(testHttpHandler);
        var tool = registry.Tools.FirstOrDefault(t => MatchesLiveTool(t, cmd.Name));
        if (tool is null)
        {
            return new KyraSlashHandleResult
            {
                HandledWithoutLlm = true,
                ResponseText = "Live tool not found. Try `/provider`.",
                SourceLabel = "Kyra · live tool"
            };
        }

        var result = await tool.ExecuteAsync(
            new KyraToolExecutionRequest
            {
                Intent = intent,
                Prompt = parse.RawInput.Trim(),
                ArgumentsLine = parse.Arguments,
                Settings = settings,
                HostFacts = facts,
                Context = new CopilotContext
                {
                    Intent = intent,
                    UserQuestion = parse.RawInput.Trim(),
                    PromptMode = CopilotPromptMode.CurrentLiveData
                }
            },
            cancellationToken).ConfigureAwait(false);

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(result.UserFacingSummary))
        {
            sb.Append(result.UserFacingSummary.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(result.ProviderAugmentation))
        {
            sb.Append(KyraLiveToolsRedactor.SanitizeForDisplay(result.ProviderAugmentation.Trim()));
        }
        else
        {
            sb.Append("No result from live tool.");
        }

        if (!string.IsNullOrWhiteSpace(result.Disclaimer))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(result.Disclaimer.Trim());
        }

        return new KyraSlashHandleResult
        {
            HandledWithoutLlm = true,
            ResponseText = sb.ToString().TrimEnd(),
            SourceLabel = "Kyra · /" + cmd.Name
        };
    }

    private static bool MatchesLiveTool(IKyraTool tool, string slashName) =>
        slashName.Equals("weather", StringComparison.OrdinalIgnoreCase) && tool.Name.Equals("Weather", StringComparison.OrdinalIgnoreCase) ||
        slashName.Equals("news", StringComparison.OrdinalIgnoreCase) && tool.Name.Equals("News", StringComparison.OrdinalIgnoreCase) ||
        slashName.Equals("stocks", StringComparison.OrdinalIgnoreCase) && tool.Name.Equals("Stocks", StringComparison.OrdinalIgnoreCase) ||
        slashName.Equals("crypto", StringComparison.OrdinalIgnoreCase) && tool.Name.Equals("Crypto", StringComparison.OrdinalIgnoreCase) ||
        slashName.Equals("sports", StringComparison.OrdinalIgnoreCase) && tool.Name.Equals("Sports", StringComparison.OrdinalIgnoreCase);
}
