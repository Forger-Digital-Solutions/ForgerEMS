using System;
using System.IO;
using System.Linq;

namespace ForgerEMS.Wpf.Tests;

public sealed class DiagnosticsSafetyViewModelTests
{
    [Fact]
    public void LinkSafetyViewModelClearsStaleOutputWhenStartingChecks()
    {
        var source = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("Previous URL result cleared", source, StringComparison.Ordinal);
        Assert.Contains("RunLinkSafetyAnalyzeAsync", source, StringComparison.Ordinal);
        Assert.Contains("DownloadLinkToQuarantineAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LinkSafetyViewModelGatesCommandsWhileRunning()
    {
        var source = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("private bool _isLinkSafetyBusy", source, StringComparison.Ordinal);
        Assert.Contains("CanRunLinkSafetyAction", source, StringComparison.Ordinal);
        Assert.Contains("DownloadLinkToQuarantineCommand.RaiseCanExecuteChanged()", source, StringComparison.Ordinal);
        Assert.Contains("SetLinkSafetyBusy(true)", source, StringComparison.Ordinal);
    }

    private static string FindRepoFile(params string[] segments)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(new[] { dir }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }

        throw new FileNotFoundException("Could not locate repo file: " + string.Join("/", segments));
    }
}
