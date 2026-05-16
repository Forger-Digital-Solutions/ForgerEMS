using System.IO;
using System.Xml.Linq;

namespace ForgerEMS.Wpf.Tests;

public class KyraSdkDogfoodPathTests
{
    [Fact]
    public void Wpf_csproj_does_not_reference_HostAdapter_or_Kyra_Sdk_directly()
    {
        var root = FindRepoRoot();
        var doc = XDocument.Load(Path.Combine(root, "src", "ForgerEMS.Wpf", "ForgerEMS.Wpf.csproj"));
        var text = File.ReadAllText(Path.Combine(root, "src", "ForgerEMS.Wpf", "ForgerEMS.Wpf.csproj"));
        var projectRefs = doc.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .ToList();
        var packageRefs = doc.Descendants("PackageReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(projectRefs, r => r.Contains("ForgerEMS.Kyra.SdkDogfood", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectRefs, r => r.Contains("ForgerEMS.Kyra.HostAdapter", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packageRefs, r => r.Contains("Kyra.Sdk", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectRefs, r => r.Contains("Kyra.Sdk", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectRefs, r => r.Contains("Kyra.Local.Core", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectRefs, r => r.Contains("Kyra.Combined.Core", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("CopyKyraSdkDogfoodTool", text, StringComparison.Ordinal);
        Assert.Contains("IncludeKyraSdkDogfoodTool", text, StringComparison.Ordinal);
        Assert.Contains("IncludeKyraSdkDogfoodTool)' == 'true'", text, StringComparison.Ordinal);
        Assert.Contains("<IncludeKyraSdkDogfoodTool Condition=", text, StringComparison.Ordinal);
        Assert.Contains(">false</IncludeKyraSdkDogfoodTool>", text, StringComparison.Ordinal);
        Assert.Contains("tools\\kyra-sdk-dogfood", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void App_startup_declares_hidden_kyra_sdk_dogfood_cli()
    {
        var app = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "ForgerEMS.Wpf", "App.xaml.cs"));
        Assert.Contains("KyraSdkDogfoodProcessLauncher", app, StringComparison.Ordinal);
        Assert.Contains("CliArgument", app, StringComparison.Ordinal);
        Assert.Contains("RunAsync(e.Args)", app, StringComparison.Ordinal);
    }

    [Fact]
    public void MainViewModel_does_not_reference_sdk_dogfood_types()
    {
        var vm = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs"));
        Assert.DoesNotContain("KyraSdkDogfood", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("ForgerEMS.Kyra.HostAdapter", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("KyraHostServiceFactory", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_xaml_unchanged_for_kyra_copilot_region()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        Assert.Contains("Kyra", xaml, StringComparison.Ordinal);
        Assert.Contains("Copilot", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kyra-sdk-dogfood", xaml, StringComparison.OrdinalIgnoreCase);
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
