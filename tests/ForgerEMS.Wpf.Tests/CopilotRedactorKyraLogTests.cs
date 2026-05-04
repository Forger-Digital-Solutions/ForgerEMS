using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>Same redaction path as <c>KyraOrchestrationLog.Append</c> (CopilotRedactor), without touching disk.</summary>
public sealed class CopilotRedactorKyraLogTests
{
    [Fact]
    public void Redact_StripsFakeApiKeyPattern()
    {
        var raw = "provider=api key: sk-fake123456789012345678901234567890 token tail";
        var safe = CopilotRedactor.Redact(raw, enabled: true);
        Assert.DoesNotContain("sk-fake", safe, StringComparison.Ordinal);
        Assert.Contains("REDACTED", safe, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Redact_StripsUsersPathPattern()
    {
        var raw = @"log=C:\Users\RealHumanName\Desktop\secret\app.log";
        var safe = CopilotRedactor.Redact(raw, enabled: true);
        Assert.DoesNotContain(@"Users\RealHumanName", safe, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REDACTED_PRIVATE_PATH", safe, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Redact_StripsSerialLikePattern()
    {
        var raw = "service tag: ABC12-3456789";
        var safe = CopilotRedactor.Redact(raw, enabled: true);
        Assert.DoesNotContain("3456789", safe, StringComparison.Ordinal);
        Assert.Contains("REDACTED_SERIAL", safe, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Redact_StripsGithubPatLikeToken()
    {
        var raw = "ghp_abcdefghijklmnopqrstuvwxyz1234567890AB";
        var safe = CopilotRedactor.Redact(raw, enabled: true);
        Assert.DoesNotContain("ghp_", safe, StringComparison.Ordinal);
        Assert.Contains("REDACTED", safe, StringComparison.OrdinalIgnoreCase);
    }
}
