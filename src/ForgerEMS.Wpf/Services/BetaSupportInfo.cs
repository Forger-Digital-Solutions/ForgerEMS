using System;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>Canonical beta support strings for UI and tests.</summary>
public static class BetaSupportInfo
{
    public const string SupportEmail = "ForgerDigitalSolutions@outlook.com";

    public const string BetaIssueSupportLine =
        "Beta issue? Send logs/screenshots to ForgerDigitalSolutions@outlook.com";

    public const string DoNotEmailSecretsWarning =
        "Do not send API keys, tokens, passwords, product keys, serial numbers, private documents, or sensitive files in support email.";

    public const string CopyrightNotice = "Copyright © 2026 Forger Digital Solutions. All rights reserved.";

    public const string WelcomeCenterFooterCopyright = "Built and Powered by © Forger Digital Solutions 2026";

    public const string MailtoSubject = "ForgerEMS Beta Issue Report";

    // Owner-set only. The public preview intentionally ships with no donation URL.
    public static string SupportDevelopmentUrl =>
        Environment.GetEnvironmentVariable("FORGEREMS_SUPPORT_DEVELOPMENT_URL")?.Trim() ?? string.Empty;

    public static bool HasConfiguredSupportDevelopmentUrl =>
        IsSafeSupportDevelopmentUrl(SupportDevelopmentUrl);

    public static bool IsSafeSupportDevelopmentUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

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
