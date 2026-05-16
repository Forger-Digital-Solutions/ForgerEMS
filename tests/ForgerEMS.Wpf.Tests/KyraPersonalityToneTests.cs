namespace ForgerEMS.Wpf.Tests;

public sealed class KyraPersonalityToneTests
{
    // UsePlayfulWording — profile-driven

    [Fact]
    public void UsePlayfulWording_BubblyTechProfile_ReturnsTrue()
    {
        Assert.True(KyraPersonalityTone.UsePlayfulWording("bubbly-tech", "How do I speed this up?"));
    }

    [Fact]
    public void UsePlayfulWording_PlayfulProfile_ReturnsTrue()
    {
        Assert.True(KyraPersonalityTone.UsePlayfulWording("playful", "check my cpu"));
    }

    [Fact]
    public void UsePlayfulWording_BalancedProfile_ReturnsFalse()
    {
        Assert.False(KyraPersonalityTone.UsePlayfulWording("balanced", "What is the CPU speed?"));
    }

    [Fact]
    public void UsePlayfulWording_NullProfile_DefaultsToBalanced_ReturnsFalse()
    {
        Assert.False(KyraPersonalityTone.UsePlayfulWording(null, "How do I update drivers?"));
    }

    // UsePlayfulWording — prompt cue overrides profile

    [Fact]
    public void UsePlayfulWording_BeSeriousCue_ReturnsFalse_EvenWhenBubbly()
    {
        Assert.False(KyraPersonalityTone.UsePlayfulWording("bubbly-tech", "be serious please"));
    }

    [Fact]
    public void UsePlayfulWording_ToneItDownCue_ReturnsFalse()
    {
        Assert.False(KyraPersonalityTone.UsePlayfulWording("bubbly", "tone it down a bit"));
    }

    [Fact]
    public void UsePlayfulWording_BeCuteCue_ReturnsTrue_EvenWhenBalanced()
    {
        Assert.True(KyraPersonalityTone.UsePlayfulWording("balanced", "be cute today"));
    }

    [Fact]
    public void UsePlayfulWording_MoreFunCue_ReturnsTrue()
    {
        Assert.True(KyraPersonalityTone.UsePlayfulWording("balanced", "can you be more fun"));
    }

    // CasualGreetingLine

    [Fact]
    public void CasualGreetingLine_Playful_IsDifferentFromSerious()
    {
        var playful = KyraPersonalityTone.CasualGreetingLine(true);
        var serious = KyraPersonalityTone.CasualGreetingLine(false);
        Assert.NotEqual(playful, serious);
    }

    [Fact]
    public void CasualGreetingLine_BothVariants_AreNonEmpty()
    {
        Assert.NotEmpty(KyraPersonalityTone.CasualGreetingLine(true));
        Assert.NotEmpty(KyraPersonalityTone.CasualGreetingLine(false));
    }

    // NormalConversationRelaxLine

    [Fact]
    public void NormalConversationRelaxLine_Playful_IsDifferentFromSerious()
    {
        Assert.NotEqual(
            KyraPersonalityTone.NormalConversationRelaxLine(true),
            KyraPersonalityTone.NormalConversationRelaxLine(false));
    }

    // FrustrationAckLine

    [Fact]
    public void FrustrationAckLine_Playful_IsDifferentFromSerious()
    {
        Assert.NotEqual(
            KyraPersonalityTone.FrustrationAckLine(true),
            KyraPersonalityTone.FrustrationAckLine(false));
    }
}
