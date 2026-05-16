using System;
using System.IO;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraBetaEnvScriptsTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ForgerEMS.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("Could not locate ForgerEMS.sln from test base directory.");
        }
    }

    [Fact]
    public void BetaScript_ExistsAndClearsOwnerKeysButKeepsLocalFallbackVars()
    {
        var text = Read("tools", "set-forgerems-beta-env.ps1");

        Assert.Contains("ClearOwnerProviderKeys", text, StringComparison.Ordinal);
        Assert.Contains("FORGEREMS_OPENAI_API_KEY", text, StringComparison.Ordinal);
        Assert.Contains("OPENAI_API_KEY", text, StringComparison.Ordinal);
        Assert.Contains("GROQ_API_KEY", text, StringComparison.Ordinal);
        Assert.Contains("OPENROUTER_API_KEY", text, StringComparison.Ordinal);
        Assert.Contains("FORGEREMS_KYRA_PROVIDER", text, StringComparison.Ordinal);
        Assert.Contains("forgerems-gateway,lmstudio,ollama,offline", text, StringComparison.Ordinal);
        Assert.DoesNotContain("FORGEREMS_OLLAMA_BASE_URL", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FORGEREMS_LMSTUDIO_BASE_URL", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Gateway token/url not ready", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Host $BetaToken", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OwnerExampleScript_IsPlaceholderOnlyAndMutuallyExclusiveCustomProvider()
    {
        var text = Read("tools", "set-forgerems-owner-env.example.ps1");

        Assert.Contains("ValidateSet(\"openrouter\", \"groq\", \"none\")", text, StringComparison.Ordinal);
        Assert.Contains("REPLACE_ME", text, StringComparison.Ordinal);
        Assert.Contains("REPLACE_WITH_BETA_ACCESS_TOKEN", text, StringComparison.Ordinal);
        Assert.Contains("Is-Placeholder", text, StringComparison.Ordinal);
        Assert.Contains("[Environment]::SetEnvironmentVariable(\"OPENROUTER_API_KEY\", $null, \"User\")", text, StringComparison.Ordinal);
        Assert.Contains("[Environment]::SetEnvironmentVariable(\"GROQ_API_KEY\", $null, \"User\")", text, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = RepoRoot;
        Array.Copy(parts, 0, all, 1, parts.Length);
        return File.ReadAllText(Path.Combine(all));
    }
}
