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
}
