using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraIntentRouterTests
{
    [Theory]
    [InlineData("Hi Kyra")]
    [InlineData("Hey Kyra")]
    [InlineData("Hello Kyra!")]
    [InlineData("Hi there Kyra")]
    [InlineData("What can you do?")]
    [InlineData("Can you help me?")]
    public void CasualGreetingAndAssistantChat_IsGeneralTech_NotForgerEms(string prompt)
    {
        Assert.Equal(KyraIntent.GeneralTechQuestion, KyraIntentRouter.DetectIntent(prompt));
    }

    [Theory]
    [InlineData("Why is Kyra offline?")]
    [InlineData("How do I configure Kyra providers?")]
    [InlineData("Where is Kyra advanced in this app?")]
    public void KyraInAppHelp_IsForgerEmsQuestion(string prompt)
    {
        Assert.Equal(KyraIntent.ForgerEMSQuestion, KyraIntentRouter.DetectIntent(prompt));
    }

    [Theory]
    [InlineData("What's the weather in Austin?")]
    [InlineData("forecast tomorrow humidity")]
    public void WeatherIntent_Detected(string prompt)
    {
        Assert.Equal(KyraIntent.Weather, KyraIntentRouter.DetectIntent(prompt));
    }

    [Theory]
    [InlineData("Bitcoin price right now")]
    [InlineData("ETH price today")]
    public void CryptoIntent_Detected(string prompt)
    {
        Assert.Equal(KyraIntent.CryptoPrice, KyraIntentRouter.DetectIntent(prompt));
    }

    [Theory]
    [InlineData("AAPL stock price")]
    [InlineData("nasdaq ticker msft")]
    public void StockIntent_Detected(string prompt)
    {
        Assert.Equal(KyraIntent.StockPrice, KyraIntentRouter.DetectIntent(prompt));
    }

    [Theory]
    [InlineData("NFL scores")]
    [InlineData("NBA playoff final score")]
    public void SportsIntent_Detected(string prompt)
    {
        Assert.Equal(KyraIntent.Sports, KyraIntentRouter.DetectIntent(prompt));
    }

    [Fact]
    public void CodeFence_TriggersCodeAssist()
    {
        const string p = "Fix this:\n```json\n{ \"a\": 1 }\n```";
        Assert.Equal(KyraIntent.CodeAssist, KyraIntentRouter.DetectIntent(p));
    }

    [Theory]
    [InlineData("What's missing before beta testing?")]
    [InlineData("Beta readiness checklist")]
    public void BetaReadiness_RoutesToForgerEms(string prompt) =>
        Assert.Equal(KyraIntent.ForgerEMSQuestion, KyraIntentRouter.DetectIntent(prompt));

    [Theory]
    [InlineData("Windows protected your PC SmartScreen")]
    [InlineData("publisher unknown unrecognized app")]
    public void SmartScreenFriction_RoutesToForgerEms(string prompt) =>
        Assert.Equal(KyraIntent.ForgerEMSQuestion, KyraIntentRouter.DetectIntent(prompt));

    [Fact]
    public void UsbMappingHowTo_RoutesToUsbBuilderHelp() =>
        Assert.Equal(KyraIntent.USBBuilderHelp, KyraIntentRouter.DetectIntent("How do I map USB ports in ForgerEMS?"));

    [Theory]
    [InlineData("What is the current law on right-to-repair today?")]
    [InlineData("Legal update today for data privacy")]
    public void LiveFactsLaw_RoutesToLiveOnlineQuestion(string prompt) =>
        Assert.Equal(KyraIntent.LiveOnlineQuestion, KyraIntentRouter.DetectIntent(prompt));

    [Fact]
    public void LatestThirdPartySoftwareRelease_RoutesToLiveOnlineQuestion() =>
        Assert.Equal(KyraIntent.LiveOnlineQuestion, KyraIntentRouter.DetectIntent("What is the newest Chrome release?"));
}
