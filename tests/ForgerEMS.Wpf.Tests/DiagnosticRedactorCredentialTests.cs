using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Credential-leak regression coverage for <see cref="DiagnosticRedactor"/>.
/// All secrets in this file are synthetic fixtures — no real credentials.
/// Guards the R3 remediation of the diagnostic environment-dump exposure:
/// env-style assignments, JSON pairs, Authorization headers, URL userinfo,
/// multiline PEM blocks, and common provider token formats must never survive
/// sanitization in logs, exception context, or support exports.
/// </summary>
public sealed class DiagnosticRedactorCredentialTests
{
    [Theory]
    [InlineData("FORGEREMS_GITHUB_TOKEN=ghp_syntheticTestValue0000000000000")]
    [InlineData("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN=synthetic-bearer-value-abcdef0123")]
    [InlineData("GITHUB_MODELS_TOKEN=github_pat_synthetic0000000000000000000")]
    [InlineData("MY_API_KEY=00000000-1111-2222-3333-444444444444")]
    [InlineData("DB_PASSWORD=synthetic-passw0rd-12345")]
    [InlineData("client_secret: synthetic-client-secret-value")]
    [InlineData("AWS_SECRET_ACCESS_KEY=synthet1cSecretKeyValue/abc123456")]
    public void RedactSecrets_StripsEnvStyleAssignments(string raw)
    {
        var safe = DiagnosticRedactor.RedactSecrets(raw);
        Assert.Contains("REDACTED", safe, StringComparison.OrdinalIgnoreCase);
        // The secret material after the separator must be gone entirely.
        var secretPart = raw.Split(new[] { '=', ':' }, 2)[1].Trim();
        Assert.DoesNotContain(secretPart, safe, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactSecrets_StripsLongEnvironmentDump()
    {
        var dump = string.Join(
            "\n",
            "PATH=C:\\Tools\\bin",
            "USERNAME=tech01",
            "FORGEREMS_GITHUB_TOKEN=ghp_syntheticTestValue0000000000000",
            "ANTHROPIC_API_KEY=sk-ant-synthetic0000000000000000000000",
            "AWS_ACCESS_KEY_ID=AKIA0000000000000SYN",
            "CF_API_TOKEN=cfut_synthetic00000000000000000000",
            "SESSION_KEY=synthetic-session-key-value-001",
            "HOMEDRIVE=C:");
        var safe = DiagnosticRedactor.RedactSecrets(dump);

        Assert.DoesNotContain("ghp_synthetic", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-ant-synthetic", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("AKIA0000000000000SYN", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("cfut_synthetic", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-session-key", safe, StringComparison.Ordinal);
        // Non-secret environment entries stay readable for diagnostics.
        Assert.Contains("HOMEDRIVE", safe, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactSecrets_StripsJsonPairs()
    {
        var raw = "{\"password\":\"synthetic-pw-001\",\"nested\":{\"client_secret\":\"synthetic-cs-999\"},\"plain\":\"visible\"}";
        var safe = DiagnosticRedactor.RedactSecrets(raw);
        Assert.DoesNotContain("synthetic-pw-001", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-cs-999", safe, StringComparison.Ordinal);
        Assert.Contains("visible", safe, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Authorization: Bearer syntheticBearerTokenValue000")]
    [InlineData("Authorization: Basic c3ludGhldGljOnBhc3N3b3Jk")]
    [InlineData("authorization: token syntheticTokenValue000")]
    public void RedactSecrets_StripsAuthorizationHeaders(string raw)
    {
        var safe = DiagnosticRedactor.RedactSecrets(raw);
        Assert.DoesNotContain("synthetic", safe, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Authorization:", safe, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedactSecrets_StripsUrlUserinfo()
    {
        var raw = "remote=https://deploy:syntheticPassword000@example.internal/repo.git";
        var safe = DiagnosticRedactor.RedactSecrets(raw);
        Assert.DoesNotContain("syntheticPassword000", safe, StringComparison.Ordinal);
        Assert.Contains("https://[REDACTED_CREDENTIAL]@example.internal", safe, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactSecrets_StripsMultilinePemBlock()
    {
        var raw = "before\n-----BEGIN RSA PRIVATE KEY-----\nMIIEsyntheticBlock\nLine2synthetic==\n-----END RSA PRIVATE KEY-----\nafter";
        var safe = DiagnosticRedactor.RedactSecrets(raw);
        Assert.DoesNotContain("syntheticBlock", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("-----BEGIN", safe, StringComparison.Ordinal);
        Assert.Contains("before", safe, StringComparison.Ordinal);
        Assert.Contains("after", safe, StringComparison.Ordinal);
        Assert.Contains("REDACTED_PRIVATE_BLOCK", safe, StringComparison.Ordinal);
    }

    [Theory]
    // Synthetic provider-format tokens (not real credentials).
    [InlineData("ghp_syntheticGitHubPat0000000000000")]
    [InlineData("gho_syntheticOauthToken000000000000")]
    [InlineData("github_pat_syntheticFineGrained0000000000")]
    [InlineData("sk-ant-syntheticAnthropicKey0000000000000")]
    [InlineData("sk-proj-syntheticOpenAiProject0000000000")]
    [InlineData("xoxb-synthetic-0000000000000-slack")]
    [InlineData("AKIA000000000000SYN1")]
    [InlineData("glpat-syntheticGitLabToken000000")]
    [InlineData("npm_syntheticNpmTokenValue0000000000000000")]
    [InlineData("AIzasyntheticGoogleApiKeyValue000000000")]
    [InlineData("ya29.syntheticGoogleOAuthToken000000")]
    [InlineData("cfut_syntheticCloudflareToken0000000000")]
    [InlineData("eyJhbGciosynthetic.eyJzdWIisynthetic.syntheticsignature0000")]
    public void RedactSecrets_StripsProviderTokenFormats(string token)
    {
        var raw = $"header saw {token} done";
        var safe = DiagnosticRedactor.RedactSecrets(raw);
        Assert.DoesNotContain(token, safe, StringComparison.Ordinal);
        Assert.Contains("REDACTED", safe, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedactSecrets_BareBearerToken_Redacted()
    {
        var raw = "Header had Bearer syntheticOpaqueToken123456 appended";
        var safe = DiagnosticRedactor.RedactSecrets(raw);
        Assert.DoesNotContain("syntheticOpaqueToken", safe, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactSecrets_LeavesNonSecretContentAlone()
    {
        var raw = "Drive Validator completed scan; 3 bad blocks; elapsed 00:12:41";
        var safe = DiagnosticRedactor.RedactSecrets(raw);
        Assert.Equal(raw, safe);
    }

    [Theory]
    [InlineData("FORGEREMS_GITHUB_TOKEN")]
    [InlineData("api_key")]
    [InlineData("clientSecret")]
    [InlineData("DB_PASSWORD")]
    [InlineData("refresh_token")]
    [InlineData("connection_string")]
    public void IsSensitiveName_FlagsCredentialNames(string name)
    {
        Assert.True(DiagnosticRedactor.IsSensitiveName(name));
    }

    [Theory]
    [InlineData("HOMEDRIVE")]
    [InlineData("PATH")]
    [InlineData("forgerems_version")]
    [InlineData("mappingRunId")]
    public void IsSensitiveName_AllowsOrdinaryNames(string name)
    {
        Assert.False(DiagnosticRedactor.IsSensitiveName(name));
    }

    [Fact]
    public void Redact_AppliesSecretAndPathRulesTogether()
    {
        var raw = "FORGEREMS_GITHUB_TOKEN=ghp_syntheticTestValue0000000000000 log=C:\\Users\\tech01\\app.log";
        var safe = DiagnosticRedactor.Redact(raw, enabled: true);
        Assert.DoesNotContain("ghp_synthetic", safe, StringComparison.Ordinal);
        Assert.DoesNotContain(@"Users\tech01", safe, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizeForLocalLog_UsesSharedSecretPatterns()
    {
        // The local-log variant keeps paths but must still strip the broadened
        // credential classes — including env-dump names and JSON pairs.
        var raw = "FORGEREMS_KYRA_GATEWAY_BETA_TOKEN=synthetic-token-abc123 path=C:\\Temp\\keepme.txt";
        var safe = UserFacingLogSanitizer.SanitizeForLocalLog(raw);
        Assert.DoesNotContain("synthetic-token-abc123", safe, StringComparison.Ordinal);
        Assert.Contains("C:\\Temp\\keepme.txt", safe, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_UsesSharedSecretPatterns()
    {
        var raw = "{\"api_key\":\"synthetic-json-secret-777\"} host=10.1.2.3";
        var safe = UserFacingLogSanitizer.Sanitize(raw);
        Assert.DoesNotContain("synthetic-json-secret-777", safe, StringComparison.Ordinal);
    }
}
