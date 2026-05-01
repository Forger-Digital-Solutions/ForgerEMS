using System;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class WslCommandRunnerTests
{
    [Fact]
    public async Task RunShellCommandAsyncWithWhitespaceReturnsPromptWithoutProcess()
    {
        var (code, text) = await WslCommandRunner.RunShellCommandAsync(
            "   ",
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(1, code);
        Assert.Contains("Enter a command", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunHostWslArgumentsAsyncWithEmptyArgsReturnsError()
    {
        var (code, text) = await WslCommandRunner.RunHostWslArgumentsAsync(
            Array.Empty<string>(),
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(1, code);
        Assert.Contains("No WSL arguments", text, StringComparison.OrdinalIgnoreCase);
    }
}
