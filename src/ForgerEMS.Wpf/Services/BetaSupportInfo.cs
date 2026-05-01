using System;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>Canonical beta support strings for UI and tests.</summary>
public static class BetaSupportInfo
{
    public const string SupportEmail = "ForgerDigitalSolutions@outlook.com";

    public const string BetaIssueSupportLine =
        "Beta issue? Send logs/screenshots to ForgerDigitalSolutions@outlook.com";

    public const string DoNotEmailSecretsWarning =
        "Do not email API keys, passwords, serial numbers, or private documents.";

    public const string CopyrightNotice = "Copyright © 2026 Forger Digital Solutions. All rights reserved.";

    public const string MailtoSubject = "ForgerEMS Beta Issue Report";

    public static string MailtoUri =>
        "mailto:" + SupportEmail +
        "?subject=" + Uri.EscapeDataString(MailtoSubject) +
        "&body=" + Uri.EscapeDataString(MailtoBodyTemplate);

    public const string MailtoBodyTemplate =
        "Version:\r\n" +
        "What happened:\r\n" +
        "Steps:\r\n" +
        "Screenshot/logs attached:\r\n";
}
