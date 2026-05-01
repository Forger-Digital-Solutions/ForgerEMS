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
}
