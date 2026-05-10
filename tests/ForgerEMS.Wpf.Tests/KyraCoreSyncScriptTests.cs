namespace ForgerEMS.Wpf.Tests;

public sealed class KyraCoreSyncScriptTests
{
    [Fact]
    public void ImportScript_ExistsAndCopiesOnlyKyraCoreFromStandalone()
    {
        var text = Read("tools", "Import-KyraCoreFromStandalone.ps1");

        Assert.Contains("KyraRepoPath", text, StringComparison.Ordinal);
        Assert.Contains("Kyra.slnx", text, StringComparison.Ordinal);
        Assert.Contains("ForgerEMS.sln", text, StringComparison.Ordinal);
        Assert.Contains("src\\Kyra.Core", text, StringComparison.Ordinal);
        Assert.Contains("SupportsShouldProcess", text, StringComparison.Ordinal);
        Assert.Contains("Kyra.App.Wpf", text, StringComparison.Ordinal);
        Assert.Contains("Assert-SafeKyraCoreText", text, StringComparison.Ordinal);
        Assert.Contains("RunValidation", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportScript_ExcludesBuildOutputsArtifactsAndGeneratedFiles()
    {
        var text = Read("tools", "Import-KyraCoreFromStandalone.ps1");

        foreach (var token in new[]
        {
            "bin",
            "obj",
            ".vs",
            "TestResults",
            "release",
            ".claude",
            ".cursor",
            "*.log",
            "*.zip",
            "*.exe",
            "*.msi",
            "*.g.cs",
            "*.AssemblyInfo.cs"
        })
        {
            Assert.Contains(token, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ImportScript_BlocksForbiddenHostStringsAndSecretPatterns()
    {
        var text = Read("tools", "Import-KyraCoreFromStandalone.ps1");

        foreach (var token in new[]
        {
            "ForgerDigitalSolutions",
            "Forger Digital Solutions",
            "USB Builder",
            "Toolkit Manager",
            "FlipValue",
            "FORGEREMS_",
            "HKLM\\Software\\ForgerEMS",
            "%LOCALAPPDATA%\\ForgerEMS",
            "api[_-]?key",
            "secret",
            "token"
        })
        {
            Assert.Contains(token, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ForgerEMS.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
    }
}
