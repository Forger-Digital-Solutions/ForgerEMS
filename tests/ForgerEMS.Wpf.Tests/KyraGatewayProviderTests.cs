using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Kyra;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraGatewayProviderTests
{
    [Fact]
    public void GatewayProviderAppearsInRegistry()
    {
        var provider = new CopilotProviderRegistry().FindById("forgerems-gateway");

        Assert.NotNull(provider);
        Assert.Equal(CopilotProviderType.ForgerEmsGateway, provider!.ProviderType);
    }

    [Fact]
    public void GatewaySelectedFirstWhenConfiguredAndApiFirst()
    {
        const string tokenEnv = "FORGEREMS_UT_GATEWAY_TOKEN_READY";
        var gateway = new KyraGatewayProvider();
        var settings = new CopilotSettings
        {
            Mode = CopilotMode.ForgerEmsBetaGateway,
            EnableFreeProviderPool = true,
            ProviderPriorityCsv = "forgerems-gateway,openrouter,offline"
        };
        settings.Providers[gateway.Id] = GatewayConfig(tokenEnv);

        try
        {
            Environment.SetEnvironmentVariable(tokenEnv, "beta-token-ready", EnvironmentVariableTarget.Process);
            var scores = KyraProviderRouter.ScoreProviders(
                [gateway],
                new CopilotRequest { Prompt = "btc price", Settings = settings },
                settings,
                new CopilotContext { UserQuestion = "btc price", Intent = KyraIntent.CryptoPrice },
                provider => settings.Providers[provider.Id]);

            var selected = Assert.Single(scores);
            Assert.Equal("forgerems-gateway", selected.Provider.Id);
        }
        finally
        {
            Environment.SetEnvironmentVariable(tokenEnv, null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void MissingGatewayUrlSkipsGateway()
    {
        const string tokenEnv = "FORGEREMS_UT_GATEWAY_TOKEN_NO_URL";
        var gateway = new EmptyDefaultGatewayProvider(tokenEnv);
        var cfg = GatewayConfig(tokenEnv);
        cfg.BaseUrl = string.Empty;

        try
        {
            Environment.SetEnvironmentVariable(tokenEnv, "beta-token", EnvironmentVariableTarget.Process);
            var resolved = KyraProviderConfigResolver.ResolveProvider(gateway, cfg);

            Assert.False(resolved.IsReady);
            Assert.Contains("base URL", resolved.SafeSkipReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(tokenEnv, null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void MissingGatewayTokenSkipsGateway()
    {
        const string tokenEnv = "FORGEREMS_UT_GATEWAY_TOKEN_MISSING";
        var gateway = new KyraGatewayProvider();
        var cfg = GatewayConfig(tokenEnv);

        Environment.SetEnvironmentVariable(tokenEnv, null, EnvironmentVariableTarget.Process);

        var resolved = KyraProviderConfigResolver.ResolveProvider(gateway, cfg);

        Assert.False(resolved.IsReady);
        Assert.Contains("API key", resolved.SafeSkipReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlaceholderGatewayTokenIsIgnored()
    {
        const string tokenEnv = "FORGEREMS_UT_GATEWAY_TOKEN_PLACEHOLDER";
        var gateway = new KyraGatewayProvider();
        var cfg = GatewayConfig(tokenEnv);

        try
        {
            Environment.SetEnvironmentVariable(tokenEnv, "REPLACE_WITH_BETA_ACCESS_TOKEN", EnvironmentVariableTarget.Process);
            var resolved = KyraProviderConfigResolver.ResolveProvider(gateway, cfg);
            Assert.False(resolved.IsReady);
            Assert.Equal(KyraProviderCredentialState.Placeholder, resolved.CredentialState);
        }
        finally
        {
            Environment.SetEnvironmentVariable(tokenEnv, null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void GatewayTokenIsRedactedInStatus()
    {
        const string tokenEnv = "FORGEREMS_UT_GATEWAY_TOKEN_STATUS";
        const string rawToken = "beta-token-super-secret";
        var gateway = new KyraGatewayProvider();
        var cfg = GatewayConfig(tokenEnv);

        try
        {
            Environment.SetEnvironmentVariable(tokenEnv, rawToken, EnvironmentVariableTarget.Process);

            var label = CopilotProviderStatusFormatter.BuildStatusLabel(gateway, cfg);
            var source = CopilotProviderStatusFormatter.BuildCredentialSourceLine(gateway, cfg);

            Assert.DoesNotContain(rawToken, label, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(rawToken, source, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Beta token: SET", label, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Gateway token source", source, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(tokenEnv, null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public async Task GatewayRateLimitResponseIsFriendlyAndTransient()
    {
        const string tokenEnv = "FORGEREMS_UT_GATEWAY_TOKEN_LIMIT";
        var client = new KyraGatewayClient(new HttpClient(new JsonHandler(
            HttpStatusCode.TooManyRequests,
            """
            {
              "ok": false,
              "errorCode": "BetaLimitReached",
              "message": "Kyra beta API time is used up for today. Local/offline mode is still available.",
              "retryAfterSeconds": 3600
            }
            """)));
        var provider = new KyraGatewayProvider(client);
        var cfg = GatewayConfig(tokenEnv);

        try
        {
            Environment.SetEnvironmentVariable(tokenEnv, "beta-token-limit", EnvironmentVariableTarget.Process);
            var result = await provider.GenerateAsync(
                new CopilotProviderRequest
                {
                    Prompt = "hello",
                    Settings = new CopilotSettings(),
                    Context = new CopilotContext { UserQuestion = "hello", Intent = KyraIntent.GeneralTechQuestion },
                    ProviderConfiguration = cfg
                },
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.True(result.IsTransientFailure);
            Assert.Equal(KyraProviderFailureReason.RateLimited, result.FailureReason);
            Assert.Contains("used up", result.UserMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("beta-token-limit", result.DiagnosticMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(tokenEnv, null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public async Task GatewayUnauthorizedResponseStaysSanitized()
    {
        const string tokenEnv = "FORGEREMS_UT_GATEWAY_TOKEN_AUTH";
        var client = new KyraGatewayClient(new HttpClient(new JsonHandler(
            HttpStatusCode.Unauthorized,
            """
            {
              "ok": false,
              "errorCode": "Unauthorized",
              "message": "Kyra beta gateway token is missing or invalid."
            }
            """)));
        var provider = new KyraGatewayProvider(client);
        var cfg = GatewayConfig(tokenEnv);

        try
        {
            Environment.SetEnvironmentVariable(tokenEnv, "beta-token-auth", EnvironmentVariableTarget.Process);
            var result = await provider.GenerateAsync(
                new CopilotProviderRequest
                {
                    Prompt = "hello",
                    Settings = new CopilotSettings(),
                    Context = new CopilotContext { UserQuestion = "hello", Intent = KyraIntent.GeneralTechQuestion },
                    ProviderConfiguration = cfg
                },
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("missing or invalid", result.UserMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("beta-token-auth", result.DiagnosticMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(tokenEnv, null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public async Task GatewayClient_SendsTokenInAuthorizationHeader_NotJsonBody()
    {
        const string token = "gateway-token-body-check";
        HttpRequestMessage? captured = null;
        string capturedBody = string.Empty;
        var client = new KyraGatewayClient(new HttpClient(new InspectingHandler(async request =>
        {
            captured = request;
            capturedBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true,"message":"ok"}""", Encoding.UTF8, "application/json")
            };
        })));

        await client.SendAsync(
            "https://gateway.example.test/v1/kyra/chat",
            new KyraGatewayRequest
            {
                BetaToken = "  " + token + "  ",
                UserMessage = "hello",
                AppVersion = "1.2.3",
                ReleaseChannel = "beta",
                LicenseTier = "community"
            },
            timeoutSeconds: 5,
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("Bearer", captured!.Headers.Authorization?.Scheme);
        Assert.Equal(token, captured.Headers.Authorization?.Parameter);
        Assert.DoesNotContain(token, capturedBody, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(capturedBody);
        Assert.False(doc.RootElement.TryGetProperty("betaToken", out _));
    }

    [Fact]
    public async Task GatewayTimeoutFallsBackWithSafeMessage()
    {
        const string tokenEnv = "FORGEREMS_UT_GATEWAY_TOKEN_TIMEOUT";
        var provider = new KyraGatewayProvider(new KyraGatewayClient(new HttpClient(new TimeoutHandler())));
        var cfg = GatewayConfig(tokenEnv);

        try
        {
            Environment.SetEnvironmentVariable(tokenEnv, "beta-token-timeout", EnvironmentVariableTarget.Process);
            var result = await provider.GenerateAsync(
                new CopilotProviderRequest
                {
                    Prompt = "hello",
                    Settings = new CopilotSettings(),
                    Context = new CopilotContext { UserQuestion = "hello", Intent = KyraIntent.GeneralTechQuestion },
                    ProviderConfiguration = cfg
                },
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(KyraProviderFailureReason.Timeout, result.FailureReason);
            Assert.Contains("timed out", result.UserMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(tokenEnv, null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void ContextSharingFalseSendsNoMachineContext()
    {
        var request = GatewayRequest(allowContext: false);
        var cfg = GatewayConfigObject(shareSystemContext: true);

        var gatewayRequest = KyraGatewayProvider.BuildGatewayRequest(request, cfg);

        Assert.Null(gatewayRequest.MachineContext);
    }

    [Fact]
    public void ContextSharingTrueSendsSanitizedMachineContextOnly()
    {
        var request = GatewayRequest(allowContext: true);
        var cfg = GatewayConfigObject(shareSystemContext: true);

        var gatewayRequest = KyraGatewayProvider.BuildGatewayRequest(request, cfg);

        Assert.NotNull(gatewayRequest.MachineContext);
        var summary = gatewayRequest.MachineContext!["summary"];
        Assert.Contains("Sanitized system context", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\Users\\Daddy_FDS", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SERIAL12345", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gateway-token", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnvStatusScriptIncludesGatewayTokenAsSecret()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "tools", "show-forgerems-env-status.ps1"));

        Assert.Contains("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN", script, StringComparison.Ordinal);
        Assert.Contains("ForgerEMS Gateway", script, StringComparison.Ordinal);
        Assert.Contains("PLACEHOLDER", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Host $v", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuditScriptTreatsReleaseProviderSecretsAsBlockers()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "tools", "audit-config-and-secrets.ps1"));

        Assert.Contains("Release blocker - provider secret in shipped/config path", script, StringComparison.Ordinal);
        Assert.Contains("Gateway beta tokens are redacted", script, StringComparison.Ordinal);
    }

    private static CopilotProviderConfiguration GatewayConfig(string tokenEnv) =>
        new()
        {
            IsEnabled = true,
            BaseUrl = "https://gateway.example.test/kyra",
            ModelName = "forgerems-gateway",
            ApiKeyEnvironmentVariable = tokenEnv,
            TimeoutSeconds = 5,
            DailyRequestCap = 60,
            MaxOutputTokens = 700
        };

    private static KyraGatewayProviderConfig GatewayConfigObject(bool shareSystemContext) =>
        new()
        {
            GatewayUrl = "https://gateway.example.test/kyra",
            BetaToken = "gateway-token",
            TimeoutSeconds = 5,
            DailyRequestLimit = 60,
            ShareSystemContext = shareSystemContext,
            UrlState = KyraProviderEndpointState.Ready,
            TokenState = KyraProviderCredentialState.FromProcessEnv
        };

    private static CopilotProviderRequest GatewayRequest(bool allowContext) =>
        new()
        {
            AppVersion = "1.2.3",
            Prompt = "What device are we working on?",
            Settings = new CopilotSettings
            {
                AllowOnlineSystemContextSharing = allowContext,
                PersonalityProfile = "bubbly-tech"
            },
            Context = new CopilotContext
            {
                UserQuestion = "What device are we working on?",
                Intent = KyraIntent.SystemHealthSummary,
                ContextText = "Path C:\\Users\\Daddy_FDS\\secret.log service tag SERIAL12345 token=gateway-token",
                SystemContext = new SystemContext
                {
                    CPU = "Intel i7",
                    GPU = "RTX 3060",
                    RAM = 32,
                    Storage = "1TB SSD",
                    OS = "Windows 11",
                    Device = "Forger Test Rig"
                }
            },
            ProviderConfiguration = new CopilotProviderConfiguration { MaxOutputTokens = 700 }
        };

    private sealed class JsonHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class InspectingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request);
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new OperationCanceledException("simulated timeout");
        }
    }

    private sealed class EmptyDefaultGatewayProvider(string tokenEnv) : ICopilotProvider
    {
        public string Id => KyraGatewayProvider.ProviderId;
        public string DisplayName => "ForgerEMS Gateway";
        public CopilotProviderType ProviderType => CopilotProviderType.ForgerEmsGateway;
        public string Category => "ForgerEMS Beta Gateway";
        public bool IsOnlineProvider => true;
        public bool IsPaidProvider => false;
        public bool EnabledByDefault => false;
        public string DefaultBaseUrl => string.Empty;
        public string DefaultModelName => "forgerems-gateway";
        public string DefaultApiKeyEnvironmentVariable => tokenEnv;
        public string StatusText => "test gateway";
        public bool IsConfigured(CopilotProviderConfiguration configuration) => false;
        public bool CanHandle(CopilotProviderRequest request) => true;
        public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new CopilotProviderResult());
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
