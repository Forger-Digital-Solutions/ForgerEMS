using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ForgerEMS.Wpf.Tests;

public sealed class ConfigurationAuditDocumentationTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ForgerEMS.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("Could not locate ForgerEMS.sln from test base directory.");
        }
    }

    [Fact]
    public void EnvironmentDocumentation_CoversKnownForgerEmsVariables()
    {
        var environment = Read("docs", "ENVIRONMENT.md");
        var audit = Read("docs", "FORGEREMS-CONFIGURATION-AUDIT.md");
        var combined = environment + Environment.NewLine + audit;

        var expected = new[]
        {
            "FORGEREMS_DEEP_SENSOR_MODE",
            "FORGEREMS_ENV",
            "FORGEREMS_RELEASE_CHANNEL",
            "FORGEREMS_PORTABLE_MODE",
            "FORGEREMS_LOG_LEVEL",
            "FORGEREMS_VERBOSE_LIVE_LOGS",
            "FORGEREMS_SUPPORT_EMAIL",
            "FORGEREMS_BACKEND_ROOT",
            "FORGEREMS_GITHUB_OWNER",
            "FORGEREMS_GITHUB_REPO",
            "FORGEREMS_UPDATE_CHANNEL",
            "FORGEREMS_UPDATE_INCLUDE_PRERELEASE",
            "FORGEREMS_UPDATE_USER_AGENT",
            "FORGEREMS_UPDATE_TIMEOUT_SECONDS",
            "FORGEREMS_KYRA_MODE",
            "FORGEREMS_KYRA_PROVIDER",
            "FORGEREMS_KYRA_ONLINE_ENABLED",
            "FORGEREMS_KYRA_SHARE_SYSTEM_CONTEXT",
            "FORGEREMS_KYRA_REQUIRE_LOCAL_FACTS",
            "FORGEREMS_KYRA_API_FIRST",
            "FORGEREMS_KYRA_PROVIDER_PRIORITY",
            "FORGEREMS_KYRA_PROVIDER_TIMEOUT_SECONDS",
            "FORGEREMS_KYRA_CONSENSUS_MODE",
            "FORGEREMS_KYRA_MEMORY_MODE",
            "FORGEREMS_KYRA_PERSIST_MEMORY",
            "FORGEREMS_KYRA_MAX_CONTEXT_TURNS",
            "FORGEREMS_KYRA_CONTEXT_MAX_CHARS",
            "FORGEREMS_KYRA_PERSONALITY",
            "FORGEREMS_OPENAI_BASE_URL",
            "FORGEREMS_OPENAI_MODEL",
            "FORGEREMS_OPENAI_API_KEY",
            "FORGEREMS_LMSTUDIO_BASE_URL",
            "FORGEREMS_LMSTUDIO_MODEL",
            "FORGEREMS_OLLAMA_BASE_URL",
            "FORGEREMS_OLLAMA_MODEL",
            "FORGEREMS_ANTHROPIC_API_KEY",
            "FORGEREMS_ANTHROPIC_MODEL",
            "FORGEREMS_GEMINI_API_KEY",
            "FORGEREMS_GEMINI_MODEL",
            "FORGEREMS_CUSTOM_PROVIDER_BASE_URL",
            "FORGEREMS_CUSTOM_PROVIDER_MODEL",
            "FORGEREMS_CUSTOM_PROVIDER_API_KEY",
            "FORGEREMS_WEATHER_PROVIDER",
            "FORGEREMS_WEATHER_API_KEY",
            "FORGEREMS_WEATHER_DEFAULT_LOCATION",
            "FORGEREMS_NEWS_PROVIDER",
            "FORGEREMS_NEWS_API_KEY",
            "FORGEREMS_FINANCE_PROVIDER",
            "FORGEREMS_FINANCE_API_KEY",
            "FORGEREMS_CRYPTO_PROVIDER",
            "FORGEREMS_CRYPTO_API_KEY",
            "FORGEREMS_STATS_PROVIDER",
            "FORGEREMS_STATS_API_KEY",
            "FORGEREMS_DIAGNOSTICS_EXPORT_DIR",
            "FORGEREMS_DIAGNOSTICS_REDACTION_STRICT",
            "FORGEREMS_ENABLE_DIAGNOSTIC_BUNDLE",
            "FORGEREMS_USB_MAPPING_DEBUG_UI",
            "FORGEREMS_MARKETPLACE_ENABLED",
            "FORGEREMS_EBAY_ENABLED",
            "FORGEREMS_EBAY_APP_ID",
            "FORGEREMS_EBAY_CERT_ID",
            "FORGEREMS_EBAY_DEV_ID",
            "FORGEREMS_MARKETPLACE_REGION",
            "FORGEREMS_VALUATION_MODE",
            "FORGEREMS_TELEMETRY_ENABLED",
            "FORGEREMS_CRASH_REPORTING_ENABLED",
            "FORGEREMS_LICENSE_TIER",
            "FORGEREMS_DEV_PROVIDER_SETTINGS",
            "FORGEREMS_FORCE_DOTNET_HASH"
        };

        foreach (var variable in expected)
        {
            Assert.Contains(variable, combined, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EnvExample_UsesOnlySafePlaceholders()
    {
        var text = Read(".env.example");

        Assert.Contains("FORGEREMS_DEEP_SENSOR_MODE=Off", text, StringComparison.Ordinal);
        Assert.Contains("REPLACE_ME", text, StringComparison.Ordinal);
        Assert.Contains("OLLAMA_BASE_URL=http://localhost:11434", text, StringComparison.Ordinal);
        Assert.Contains("LM_STUDIO_BASE_URL=http://localhost:1234/v1", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(RealSecretRegex(), text);
    }

    [Fact]
    public void PrivacyAndLegal_DocumentOnlineProviderBehavior()
    {
        var privacy = Read("docs", "PRIVACY.md");
        var legal = Read("docs", "LEGAL.md");

        Assert.Contains("Offline / local Kyra", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Optional online", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("System Intelligence context", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("online AI provider", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Optional online providers", legal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sanitized context", legal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not paste API keys, tokens, passwords", legal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeepSensorAndProviderPaths_AreDocumented()
    {
        var environment = Read("docs", "ENVIRONMENT.md");
        var audit = Read("docs", "FORGEREMS-CONFIGURATION-AUDIT.md");
        var combined = environment + Environment.NewLine + audit;

        Assert.Contains("FORGEREMS_DEEP_SENSOR_MODE", combined, StringComparison.Ordinal);
        Assert.Contains("providers/sensors/LibreHardwareMonitorLib.dll", combined, StringComparison.Ordinal);
        Assert.Contains("providers/sensors/THIRD-PARTY-NOTICES.txt", combined, StringComparison.Ordinal);
        Assert.Contains("providers/sensors/LICENSES/", combined, StringComparison.Ordinal);
        Assert.Contains("LibreHardwareMonitor", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void GitIgnore_ExcludesCommonSecretAndGeneratedFiles()
    {
        var text = Read(".gitignore");
        var expected = new[]
        {
            ".env",
            ".env.*",
            "!.env.example",
            "secrets.json",
            "tokens.json",
            "appsettings.Production.json",
            "*.pfx",
            "*.pem",
            "*.key",
            "artifacts/"
        };

        foreach (var pattern in expected)
        {
            Assert.Contains(pattern, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ConfigurationAuditScript_IsLocalRedactingAndWritesArtifact()
    {
        var text = Read("tools", "audit-config-and-secrets.ps1");

        Assert.Contains("artifacts/config-audit/forgerems-config-audit.txt", text, StringComparison.Ordinal);
        Assert.Contains("Get-RedactedPreview", text, StringComparison.Ordinal);
        Assert.Contains("upload data", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gitleaks", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trufflehog", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-RestMethod", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SourceControlledText_DoesNotContainObviousRealSecrets()
    {
        var regex = RealSecretRegex();
        var allowlistedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Normalize("tools/audit-config-and-secrets.ps1"),
            Normalize("tools/check-secrets.ps1")
        };

        foreach (var file in EnumerateScannableTextFiles())
        {
            var relative = Normalize(Path.GetRelativePath(RepoRoot, file.FullName));
            if (allowlistedFiles.Contains(relative))
            {
                continue;
            }

            var text = File.ReadAllText(file.FullName);
            Assert.False(regex.IsMatch(text), $"Secret-like value found in {relative}. Redact or move to local secret storage.");
        }
    }

    [Fact]
    public void ConfigurationAuditDoc_ClassifiesSecretsAndActionItems()
    {
        var text = Read("docs", "FORGEREMS-CONFIGURATION-AUDIT.md");

        Assert.Contains("Secret Scanning Findings", text, StringComparison.Ordinal);
        Assert.Contains("No obvious real-looking cloud API keys", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rotate it immediately", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Files That Must Not Be Committed", text, StringComparison.Ordinal);
        Assert.Contains("Support-Report Redaction Requirements", text, StringComparison.Ordinal);
        Assert.Contains("Must fix before beta", text, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = RepoRoot;
        Array.Copy(parts, 0, all, 1, parts.Length);
        return File.ReadAllText(Path.Combine(all));
    }

    private static IEnumerable<FileInfo> EnumerateScannableTextFiles()
    {
        var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", "bin", "obj", "dist", "release", "artifacts", "tests"
        };
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".xaml", ".ps1", ".md", ".txt", ".json", ".iss", ".yml", ".yaml",
            ".csproj", ".props", ".targets", ".config", ".xml", ".bat", ".cmd", ".example"
        };

        return Directory.EnumerateFiles(RepoRoot, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(file =>
            {
                var relative = Path.GetRelativePath(RepoRoot, file.FullName);
                var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (parts.Any(part => excludedDirs.Contains(part)))
                {
                    return false;
                }

                if (file.Name is ".env.example" or ".gitignore")
                {
                    return true;
                }

                return extensions.Contains(file.Extension);
            });
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static Regex RealSecretRegex()
    {
        return new Regex(
            @"sk-[A-Za-z0-9_-]{20,}|sk-ant-[A-Za-z0-9_-]{20,}|gsk_[A-Za-z0-9_-]{20,}|github_pat_[A-Za-z0-9_]{20,}|(ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9_]{20,}|AIza[0-9A-Za-z_-]{20,}|AKIA[0-9A-Z]{16}|xox[baprs]-[A-Za-z0-9-]{12,}|-----BEGIN (RSA |EC |OPENSSH |)PRIVATE KEY-----",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }
}
