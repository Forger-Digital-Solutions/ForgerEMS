using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class WslCommandRunnerCancellationTests
{
    [Fact]
    public async Task RunShellCommandAsyncPreCancelledTokenDoesNotThrow()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var (code, text) = await WslCommandRunner.RunShellCommandAsync("echo 1", TimeSpan.FromSeconds(5), cts.Token);

        if (WslCommandRunner.IsWslInstalled())
        {
            Assert.Equal(130, code);
            Assert.Contains("cancelled", text, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Equal(1, code);
            Assert.Contains("WSL", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
