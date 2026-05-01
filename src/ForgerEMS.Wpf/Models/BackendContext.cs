using System;
using System.IO;

namespace VentoyToolkitSetup.Wpf.Models;

public enum BackendMode
{
    Unknown = 0,
    Bundled = 1,
    Repo = 2,
    ReleaseBundle = 3
}

public sealed class BackendContext
{
    public bool IsAvailable { get; init; }

    public BackendMode Mode { get; init; }

    public string RootPath { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    public string VerifyScriptPath { get; init; } = string.Empty;

    public string SetupScriptPath { get; init; } = string.Empty;

    public string UpdateScriptPath { get; init; } = string.Empty;

    public string DiagnosticMessage { get; init; } = string.Empty;

    public string FrontendVersion { get; init; } = string.Empty;

    public string BackendVersion { get; init; } = string.Empty;

    public string ModeLabel =>
        Mode switch
        {
            BackendMode.Bundled => "Bundled backend",
            BackendMode.Repo => "Repo mode",
            BackendMode.ReleaseBundle => "Release-bundle mode",
            _ => "Backend unavailable"
        };

    public static string PrimaryManagedSummaryPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ForgerEMS",
            ".verify",
            "managed-download-revalidation",
            "latest",
            "managed-download-summary.txt");

    public string ReleaseVerificationHistoryRoot =>
        string.IsNullOrWhiteSpace(RootPath)
            ? string.Empty
            : Path.Combine(RootPath, "docs", "Release-Verification-History");

    public string PrimaryManifestPath =>
        string.IsNullOrWhiteSpace(RootPath)
            ? string.Empty
            : Path.Combine(RootPath, "ForgerEMS.updates.json");

    public string RepoManifestPath =>
        string.IsNullOrWhiteSpace(RootPath)
            ? string.Empty
            : Path.Combine(RootPath, "manifests", "ForgerEMS.updates.json");

    public string ReleaseVentoyManualNotePath =>
        string.IsNullOrWhiteSpace(RootPath)
            ? string.Empty
            : Path.Combine(RootPath, "docs", "IfScriptFails(ManualSetup)", "Ventoy_Official.txt");

    public string RepoVentoyManualNotePath =>
        string.IsNullOrWhiteSpace(RootPath)
            ? string.Empty
            : Path.Combine(RootPath, "docs", "ventoy-core", "bundle", "IfScriptFails(ManualSetup)", "Ventoy_Official.txt");

    public static BackendContext Unavailable(string message, string frontendVersion = "")
    {
        return new BackendContext
        {
            IsAvailable = false,
            Mode = BackendMode.Unknown,
            FrontendVersion = frontendVersion,
            DiagnosticMessage = message
        };
    }
}
