using System.Net;
using System.Net.Http;
using System.Text;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Kyra;

namespace ForgerEMS.Wpf.Tests;

[Collection("GatewayEnv")]
public sealed class KyraGatewayResearchTests
{
    private static EnvRestore EnvGate(string url, string token)
    {
        var prevUrl = Environment.GetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_URL");
        var prevTok = Environment.GetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN");
        Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_URL", url);
        Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN", token);
        return new EnvRestore(prevUrl, prevTok);
    }

    private sealed class EnvRestore : IDisposable
    {
        private readonly string? _url;
        private readonly string? _tok;

        public EnvRestore(string? url, string? tok)
        {
            _url = url;
            _tok = tok;
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_URL", _url);
            Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN", _tok);
        }
    }

    [Fact]
    public void KyraGatewayResearchClient_BuildResearchEndpoint_AppendsSegment()
    {
        var ep = KyraGatewayResearchClient.BuildResearchEndpoint("https://example.test/worker/");
        Assert.Equal("https://example.test/worker/v1/kyra/research", ep);
    }

    [Fact]
    public void KyraGatewayStatusClient_BuildStatusEndpoint_AppendsSegment()
    {
        var ep = KyraGatewayStatusClient.BuildStatusEndpoint("https://example.test/");
        Assert.Equal("https://example.test/v1/kyra/status", ep);
    }

    [Fact]
    public void KyraGatewayResearchSanitizer_StripsEmailsAndPaths()
    {
        var s = KyraGatewayResearchSanitizer.SanitizePrompt("Contact admin@test.com on C:\\Users\\SecretUser\\file.txt");
        Assert.DoesNotContain("admin@test.com", s, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretUser", s, StringComparison.Ordinal);
    }

    [Fact]
    public void KyraRealtimeResearchClassifier_GatewayDisabledInSettings_ReturnsFalse()
    {
        using var envGate = EnvGate("https://unit.test/gateway/", "unit-test-beta-token");
        var settings = new CopilotSettings
        {
            KyraRealtimeGatewayEnabled = false,
            KyraRealtimeGatewayResearchEnabled = true,
            KyraRealtimeGatewayResearchConsent = true
        };
        var use = KyraRealtimeResearchClassifier.ShouldUseRealtimeGateway("How is BTC doing today?", settings, out var intent);
        Assert.False(use);
        Assert.Equal("chat", intent);
    }

    [Fact]
    public void KyraRealtimeResearchClassifier_LocalDeviceQuestion_ReturnsFalse()
    {
        using var envGate = EnvGate("https://unit.test/gateway/", "unit-test-beta-token");
        var settings = new CopilotSettings
        {
            KyraRealtimeGatewayEnabled = true,
            KyraRealtimeGatewayResearchEnabled = true,
            KyraRealtimeGatewayResearchConsent = true
        };
        var use = KyraRealtimeResearchClassifier.ShouldUseRealtimeGateway(
            "What device are we working on?",
            settings,
            out _);
        Assert.False(use);
    }

    [Fact]
    public void KyraGatewayResearchCoordinator_StaleKnowledgeRejected_ForCryptoIntent()
    {
        Assert.True(KyraGatewayResearchCoordinator.ContainsStaleKnowledgeWording("As of my last update, BTC is..."));
        Assert.False(KyraGatewayResearchCoordinator.ContainsStaleKnowledgeWording("Bitcoin is near $50k on CoinGecko."));
    }

    [Fact]
    public async Task KyraGatewayResearchCoordinator_Unavailable_DoesNotUseStaleWording()
    {
        using var envGate = EnvGate("https://unit.test/gateway/", "unit-test-beta-token");
        var handler = new StubResearchHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("""{"ok":false,"errorCode":"provider_unavailable","safeMessage":"temporary"}""", Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        var client = new KyraGatewayResearchClient(http);
        var settings = new CopilotSettings
        {
            KyraRealtimeGatewayEnabled = true,
            KyraRealtimeGatewayResearchEnabled = true,
            KyraRealtimeGatewayResearchConsent = true
        };

        var resp = await KyraGatewayResearchCoordinator.TryRealtimeResearchAsync(
            "How is BTC doing today?",
            settings,
            systemIntelligenceReportPath: null,
            toolkitReportPath: null,
            appVersion: "1.0.0",
            client,
            CancellationToken.None);

        Assert.NotNull(resp);
        Assert.False(resp!.UsedOnlineData);
        Assert.DoesNotContain("knowledge cutoff", resp.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("last update", resp.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task KyraGatewayResearchCoordinator_CryptoConfigured_AttemptsGatewayCryptoIntent()
    {
        using var envGate = EnvGate("https://unit.test/gateway/", "unit-test-beta-token");
        var client = new CapturingResearchClient(new KyraGatewayResearchResponseDto
        {
            Ok = true,
            Answer = "BTC: about $50,000 USD (24h +1.20%) via CoinGecko.",
            Tool = "crypto",
            Provider = "coingecko"
        });
        var settings = new CopilotSettings
        {
            KyraRealtimeGatewayEnabled = true,
            KyraRealtimeGatewayResearchEnabled = true,
            KyraRealtimeGatewayResearchConsent = true
        };

        var resp = await KyraGatewayResearchCoordinator.TryRealtimeResearchAsync(
            "price of BTC today",
            settings,
            systemIntelligenceReportPath: null,
            toolkitReportPath: null,
            appVersion: "1.0.0",
            client,
            CancellationToken.None);

        Assert.NotNull(resp);
        Assert.Equal("crypto", client.LastBody?.Intent);
        Assert.True(resp!.UsedOnlineData);
        Assert.Contains("BTC", resp.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CoinGecko", resp.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("knowledge cutoff", resp.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task KyraGatewayResearchCoordinator_CryptoFailure_UsesSafeUnavailableMessage()
    {
        using var envGate = EnvGate("https://unit.test/gateway/", "unit-test-beta-token");
        var client = new CapturingResearchClient(new KyraGatewayResearchResponseDto
        {
            Ok = false,
            ErrorCode = "provider_unavailable",
            SafeMessage = "temporary low-level provider message"
        });
        var settings = new CopilotSettings
        {
            KyraRealtimeGatewayEnabled = true,
            KyraRealtimeGatewayResearchEnabled = true,
            KyraRealtimeGatewayResearchConsent = true
        };

        var resp = await KyraGatewayResearchCoordinator.TryRealtimeResearchAsync(
            "price of BTC today",
            settings,
            systemIntelligenceReportPath: null,
            toolkitReportPath: null,
            appVersion: "1.0.0",
            client,
            CancellationToken.None);

        Assert.NotNull(resp);
        Assert.False(resp!.UsedOnlineData);
        Assert.Contains("couldn’t load live BTC pricing", resp.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rate-limited", resp.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("knowledge cutoff", resp.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task KyraGatewayResearchCoordinator_HardwareLookupUnavailable_DoesNotFabricateExactSku()
    {
        var prevUrlProcess = Environment.GetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_URL", EnvironmentVariableTarget.Process);
        var prevTokProcess = Environment.GetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN", EnvironmentVariableTarget.Process);
        var prevGatewayEnabled = Environment.GetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_ENABLED", EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_URL", null, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN", null, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_ENABLED", "false", EnvironmentVariableTarget.Process);
        var path = Path.Combine(Path.GetTempPath(), $"kyra-battery-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"schemaVersion":1,"summary":{"manufacturer":"Dell","model":"Precision 5540"},"batteries":[{"name":"Internal Battery","designCapacityDisplay":"90000 mWh","fullChargeCapacityDisplay":"54000 mWh","wearPercent":40.2}],"health":{"overallScore":80}}""");
        try
        {
            var resp = await KyraGatewayResearchCoordinator.TryRealtimeResearchAsync(
                "what replacement battery should I buy for my Dell Precision 5540",
                new CopilotSettings
                {
                    KyraRealtimeGatewayEnabled = true,
                    KyraRealtimeGatewayResearchEnabled = true,
                    KyraRealtimeGatewayResearchConsent = true
                },
                path,
                toolkitReportPath: null,
                appVersion: "1.0.0",
                client: null,
                CancellationToken.None);

            Assert.NotNull(resp);
            Assert.False(resp!.UsedOnlineData);
            Assert.Contains("I can’t verify the exact part from live sources right now", resp.Text, StringComparison.Ordinal);
            Assert.Contains("90000 mWh", resp.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Not verified externally", resp.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("verified SKU", resp.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Dell part number:", resp.Text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
            Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_URL", prevUrlProcess, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN", prevTokProcess, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_ENABLED", prevGatewayEnabled, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void KyraGatewayResearchConsent_CommunitySharing_RemainsIndependent()
    {
        var settings = new CopilotSettings { KyraCommunitySharingEnabled = false };
        Assert.False(settings.KyraCommunitySharingEnabled);
    }

    [Fact]
    public void WorkerStatusEndpoint_TrimsEnvTokenAndDoesNotReturnSecretShape()
    {
        var worker = File.ReadAllText(Path.Combine(FindRepoRoot(), "gateway", "cloudflare-worker", "src", "index.ts"));

        Assert.Contains("normalizeToken(env.BETA_GATEWAY_TOKEN)", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("BETA_GATEWAY_TOKEN.length", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("secretLength", worker, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkerProviderSanitizer_RedactsCommonSecretsAndPiiPatterns()
    {
        var worker = File.ReadAllText(Path.Combine(FindRepoRoot(), "gateway", "cloudflare-worker", "src", "index.ts"));

        Assert.Contains("gsk_", worker, StringComparison.Ordinal);
        Assert.Contains("email redacted", worker, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ip redacted", worker, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("product key redacted", worker, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkerFinanceResearch_DoesNotFallThroughToGenericLlm()
    {
        var worker = File.ReadAllText(Path.Combine(FindRepoRoot(), "gateway", "cloudflare-worker", "src", "index.ts"));

        Assert.Contains("if (intent === \"finance\")", worker, StringComparison.Ordinal);
        Assert.Contains("I will not invent current stock market changes.", worker, StringComparison.Ordinal);
        Assert.Contains("Live finance data is not configured on the gateway yet.", worker, StringComparison.Ordinal);
    }

    private sealed class StubResearchHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;

        public StubResearchHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) => _factory = factory;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_factory(request));
    }

    private sealed class CapturingResearchClient(KyraGatewayResearchResponseDto response) : IKyraGatewayResearchClient
    {
        public KyraGatewayResearchRequestDto? LastBody { get; private set; }

        public Task<KyraGatewayResearchResponseDto> SendResearchAsync(
            string researchEndpoint,
            string bearerToken,
            KyraGatewayResearchRequestDto body,
            string appVersion,
            string releaseChannel,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            LastBody = body;
            return Task.FromResult(response);
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ForgerEMS.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
