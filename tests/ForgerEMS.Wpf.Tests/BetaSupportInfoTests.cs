using System;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class BetaSupportInfoTests
{
    [Fact]
    public void SupportEmailIsOutlookContact()
    {
        Assert.Equal("ForgerDigitalSolutions@outlook.com", BetaSupportInfo.SupportEmail);
    }

    [Fact]
    public void CopyrightContainsYearAndCompany()
    {
        Assert.Contains("2026", BetaSupportInfo.CopyrightNotice, StringComparison.Ordinal);
        Assert.Contains("Forger Digital Solutions", BetaSupportInfo.CopyrightNotice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MailtoUri_ContainsSupportAddressAndSubject()
    {
        Assert.StartsWith("mailto:", BetaSupportInfo.MailtoUri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ForgerDigitalSolutions@outlook.com", BetaSupportInfo.MailtoUri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("subject=", BetaSupportInfo.MailtoUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DoNotEmailSecretsWarning_IsPresent()
    {
        Assert.Contains("API keys", BetaSupportInfo.DoNotEmailSecretsWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("passwords", BetaSupportInfo.DoNotEmailSecretsWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tokens", BetaSupportInfo.DoNotEmailSecretsWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("product keys", BetaSupportInfo.DoNotEmailSecretsWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BetaIssueSupportLine_ContainsSupportEmail()
    {
        Assert.Contains(BetaSupportInfo.SupportEmail, BetaSupportInfo.BetaIssueSupportLine, StringComparison.OrdinalIgnoreCase);
    }
}
