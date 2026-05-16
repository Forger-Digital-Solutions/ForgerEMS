namespace ForgerEMS.Kyra.HostAdapter.Tests;

public class KyraSdkPackageFeedCiTests
{
    [Fact]
    public void Validate_script_supports_feed_path_parameter_and_env_var()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "tools", "Validate-KyraSdkPackageFeed.ps1"));
        Assert.Contains("-KyraSdkFeedPath", script, StringComparison.Ordinal);
        Assert.Contains("KYRA_SDK_FEED_PATH", script, StringComparison.Ordinal);
        Assert.Contains("nuget.config.ci", script, StringComparison.Ordinal);
        Assert.Contains("UseKyraSdkProjectReference=false", script, StringComparison.Ordinal);
        Assert.Contains("ForgerEMS.Kyra.HostAdapter.Tests", script, StringComparison.Ordinal);
        Assert.Contains("ForgerEMS.Wpf.Tests", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Gitignore_excludes_generated_nuget_config_ci()
    {
        var gitignore = File.ReadAllText(Path.Combine(FindRepoRoot(), ".gitignore"));
        Assert.Contains("nuget.config.ci", gitignore, StringComparison.Ordinal);
    }

    [Fact]
    public void Package_mode_workflow_supports_cross_repo_artifact_handoff()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRepoRoot(), ".github", "workflows", "kyra-sdk-package-mode.yml"));
        Assert.Contains("repository_dispatch", workflow, StringComparison.Ordinal);
        Assert.Contains("kyra-sdk-feed-published", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/download-artifact@v4", workflow, StringComparison.Ordinal);
        Assert.Contains("KYRA_SDK_ARTIFACT_TOKEN", workflow, StringComparison.Ordinal);
        Assert.Contains("Validate-KyraSdkPackageFeed.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("KYRA_SDK_FEED_PATH", workflow, StringComparison.Ordinal);
        Assert.Contains("kyra_sdk_feed_path", workflow, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ForgerEMS.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate ForgerEMS repo root.");
    }
}
