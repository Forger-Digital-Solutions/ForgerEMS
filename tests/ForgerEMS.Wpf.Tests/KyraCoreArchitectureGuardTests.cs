namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Enforces that Kyra.Core stays free of ForgerEMS-specific coupling.
/// These tests read source files as text so they catch violations at the code level,
/// not just at the assembly level.
/// </summary>
public sealed class KyraCoreArchitectureGuardTests
{
    private static readonly string[] ForbiddenTerms =
    [
        "ForgerEMS",
        "USB Builder",
        "Toolkit Manager",
        "%LOCALAPPDATA%\\ForgerEMS",
        "HKLM\\Software\\ForgerEMS",
        "FORGEREMS_",
        "ForgerDigitalSolutions",
        "Forger Digital Solutions",
    ];

    [Fact]
    public void KyraCore_SourceFilesContainNoForgerEMSSpecificStrings()
    {
        var root = FindRepoRoot();
        var kyraCoreSrcDir = Path.Combine(root, "src", "Kyra.Core");
        Assert.True(Directory.Exists(kyraCoreSrcDir), $"Kyra.Core src directory not found at {kyraCoreSrcDir}");

        var files = Directory.GetFiles(kyraCoreSrcDir, "*.cs", SearchOption.AllDirectories)
            .Append(Path.Combine(kyraCoreSrcDir, "Kyra.Core.csproj"))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Where(File.Exists)
            .ToArray();
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            var fileName = Path.GetFileName(file);
            foreach (var term in ForbiddenTerms)
            {
                Assert.False(
                    content.Contains(term, StringComparison.OrdinalIgnoreCase),
                    $"Kyra.Core file '{fileName}' contains forbidden ForgerEMS-specific term: '{term}'");
            }
        }
    }

    [Fact]
    public void KyraCore_ProjectFileDoesNotReferenceForgerEMSWpf()
    {
        var csprojPath = Path.Combine(FindRepoRoot(), "src", "Kyra.Core", "Kyra.Core.csproj");
        Assert.True(File.Exists(csprojPath), $"Kyra.Core.csproj not found at {csprojPath}");

        var content = File.ReadAllText(csprojPath);
        Assert.DoesNotContain("ForgerEMS.Wpf", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraCore_ProjectFileTargetsNetStandard_NotWindowsSpecific()
    {
        var csprojPath = Path.Combine(FindRepoRoot(), "src", "Kyra.Core", "Kyra.Core.csproj");
        var content = File.ReadAllText(csprojPath);

        Assert.DoesNotContain("net8.0-windows", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("net8.0", content, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ForgerEMS.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
