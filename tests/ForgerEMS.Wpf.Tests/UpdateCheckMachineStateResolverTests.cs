using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class UpdateCheckMachineStateResolverTests
{
    [Fact]
    public void InProgress_AlwaysChecking()
    {
        var s = UpdateCheckMachineStateResolver.Resolve(
            updateCheckInProgress: true,
            new UpdateCheckResult { Succeeded = true, Outcome = UpdateCheckOutcome.AlreadyLatest });
        Assert.Equal(UpdateCheckMachineState.Checking, s);
    }

    [Fact]
    public void NullLast_IdleNotChecked()
    {
        var s = UpdateCheckMachineStateResolver.Resolve(false, null);
        Assert.Equal(UpdateCheckMachineState.IdleNotChecked, s);
    }

    [Fact]
    public void NetworkFailure_Offline()
    {
        var s = UpdateCheckMachineStateResolver.Resolve(
            false,
            new UpdateCheckResult
            {
                Succeeded = false,
                Outcome = UpdateCheckOutcome.Failed,
                FailureKind = UpdateCheckFailureKind.Network
            });
        Assert.Equal(UpdateCheckMachineState.FailedOffline, s);
    }

    [Fact]
    public void RateLimitMessage_RateLimitedState()
    {
        var s = UpdateCheckMachineStateResolver.Resolve(
            false,
            new UpdateCheckResult
            {
                Succeeded = false,
                Outcome = UpdateCheckOutcome.Failed,
                FailureKind = UpdateCheckFailureKind.AccessDeniedOrRateLimited,
                ErrorMessage = "GitHub rate-limited this device."
            });
        Assert.Equal(UpdateCheckMachineState.FailedRateLimited, s);
    }

    [Fact]
    public void ForbiddenWithoutRateKeyword_GitHubBucket()
    {
        var s = UpdateCheckMachineStateResolver.Resolve(
            false,
            new UpdateCheckResult
            {
                Succeeded = false,
                Outcome = UpdateCheckOutcome.Failed,
                FailureKind = UpdateCheckFailureKind.AccessDeniedOrRateLimited,
                ErrorMessage = "GitHub denied access (403)."
            });
        Assert.Equal(UpdateCheckMachineState.FailedGitHub, s);
    }

    [Fact]
    public void NoSuitableAssets_Mapped()
    {
        var s = UpdateCheckMachineStateResolver.Resolve(
            false,
            new UpdateCheckResult { Succeeded = true, Outcome = UpdateCheckOutcome.NoSuitableAssets });
        Assert.Equal(UpdateCheckMachineState.NoSuitableAssets, s);
    }

    [Fact]
    public void InstalledParse_ConfigError()
    {
        var s = UpdateCheckMachineStateResolver.Resolve(
            false,
            new UpdateCheckResult
            {
                Succeeded = false,
                Outcome = UpdateCheckOutcome.Failed,
                FailureKind = UpdateCheckFailureKind.ReleaseMetadataInvalid,
                ErrorMessage = "Could not determine the installed app version for comparison."
            });
        Assert.Equal(UpdateCheckMachineState.ConfigError, s);
    }
}
