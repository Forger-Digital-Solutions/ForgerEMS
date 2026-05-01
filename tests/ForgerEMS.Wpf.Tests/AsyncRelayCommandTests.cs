using System;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Infrastructure;

namespace ForgerEMS.Wpf.Tests;

public sealed class AsyncRelayCommandTests
{
    [Fact]
    public async Task ExecuteSwallowsExceptionFromAsyncHandler()
    {
        var cmd = new AsyncRelayCommand(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("expected test exception");
        });

        cmd.Execute(null);
        await Task.Delay(400);
    }

    [Fact]
    public async Task ExecuteSwallowsOperationCanceled()
    {
        var cmd = new AsyncRelayCommand(async () =>
        {
            await Task.Yield();
            throw new OperationCanceledException();
        });

        cmd.Execute(null);
        await Task.Delay(200);
    }
}
