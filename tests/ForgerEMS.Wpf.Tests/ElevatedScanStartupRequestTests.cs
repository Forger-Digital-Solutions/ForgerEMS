using System;
using VentoyToolkitSetup.Wpf.Services.Intelligence;

namespace ForgerEMS.Wpf.Tests;

public sealed class ElevatedScanStartupRequestTests
{
    [Fact]
    public void Parse_RecognizesElevatedScanResumeArguments()
    {
        var requestId = Guid.NewGuid().ToString("N");
        var request = ElevatedScanStartupRequest.Parse(
            [
                "--open-system-intelligence",
                "--run-elevated-scan",
                "--elevated-scan-request-id",
                requestId
            ]);

        Assert.True(request.OpenSystemIntelligence);
        Assert.True(request.RunElevatedScan);
        Assert.True(request.HasPendingElevatedScan);
        Assert.Equal(requestId, request.RequestId);
    }

    [Fact]
    public void Parse_InvalidRequestId_DoesNotCreatePendingLoop()
    {
        var request = ElevatedScanStartupRequest.Parse(
            [
                "--open-system-intelligence",
                "--run-elevated-scan",
                "--elevated-scan-request-id",
                "not a guid"
            ]);

        Assert.True(request.OpenSystemIntelligence);
        Assert.True(request.RunElevatedScan);
        Assert.False(request.HasPendingElevatedScan);
        Assert.Equal(string.Empty, request.RequestId);
    }

    [Fact]
    public void AddArguments_UsesSeparateArgumentTokens()
    {
        var requestId = Guid.NewGuid().ToString("N");
        var builder = new ProcessStartInfoBuilder();

        ElevatedScanStartupRequest.AddArguments(builder, requestId);

        Assert.Equal(
            [
                "--open-system-intelligence",
                "--run-elevated-scan",
                "--elevated-scan-request-id",
                requestId
            ],
            builder.Arguments);
    }
}
