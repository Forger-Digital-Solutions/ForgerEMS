using System.IO;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraProviderCredentialStoreTests
{
    [Fact]
    public void ProtectedCredentialStore_DoesNotWritePlaintextAndCanClear()
    {
        var path = Path.Combine(Path.GetTempPath(), "forgerems-tests", Guid.NewGuid().ToString("N"), "kyra-credentials.protected.json");
        var store = new KyraProviderCredentialStore(path);
        if (!store.IsProtectedLocalStorageAvailable)
        {
            return;
        }

        const string provider = "openai-compatible";
        const string secret = "fds-test-secret-1234";

        Assert.True(store.SaveSecret(provider, secret, out var status), status);
        Assert.True(File.Exists(path));
        Assert.DoesNotContain(secret, File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal(secret, store.TryGetSecret(provider));
        Assert.Equal("Protected local key present", store.BuildSanitizedStatus(provider));

        store.ClearSecret(provider);
        Assert.False(store.HasSecret(provider));
        Assert.Empty(store.TryGetSecret(provider));
        Assert.DoesNotContain(secret, File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderCredentialResolution_PrefersSessionThenProtectedThenEnvironment()
    {
        var path = Path.Combine(Path.GetTempPath(), "forgerems-tests", Guid.NewGuid().ToString("N"), "kyra-credentials.protected.json");
        var store = new KyraProviderCredentialStore(path);
        if (!store.IsProtectedLocalStorageAvailable)
        {
            return;
        }

        KyraProviderCredentialStore.UseDefaultForTests(store);
        const string provider = "openai-compatible";
        const string envName = "FORGEREMS_TEST_OPENAI_COMPATIBLE_KEY";

        try
        {
            Environment.SetEnvironmentVariable(envName, "env-key", EnvironmentVariableTarget.Process);
            Assert.True(store.SaveSecret(provider, "encrypted-key", out var status), status);
            KyraApiKeyStore.SetSessionKey(provider, "session-key");

            var session = ProviderEnvironmentResolver.ResolveApiCredential(provider, envName);
            Assert.Equal(KyraCredentialSource.Session, session.Source);
            Assert.Equal("session-key", session.Value);

            KyraApiKeyStore.ClearSessionKey(provider);
            var encrypted = ProviderEnvironmentResolver.ResolveApiCredential(provider, envName);
            Assert.Equal(KyraCredentialSource.EncryptedLocal, encrypted.Source);
            Assert.Equal("encrypted-key", encrypted.Value);

            store.ClearSecret(provider);
            var env = ProviderEnvironmentResolver.ResolveApiCredential(provider, envName);
            Assert.Equal(KyraCredentialSource.ProcessEnvironment, env.Source);
            Assert.Equal("env-key", env.Value);
        }
        finally
        {
            KyraApiKeyStore.ClearSessionKey(provider);
            store.ClearSecret(provider);
            Environment.SetEnvironmentVariable(envName, null, EnvironmentVariableTarget.Process);
            KyraProviderCredentialStore.UseDefaultForTests(new KyraProviderCredentialStore(
                Path.Combine(Path.GetTempPath(), "forgerems-tests", Guid.NewGuid().ToString("N"), "empty.protected.json")));
        }
    }

    [Fact]
    public void ProviderCredentialStatus_NeverIncludesRawSecret()
    {
        var path = Path.Combine(Path.GetTempPath(), "forgerems-tests", Guid.NewGuid().ToString("N"), "kyra-credentials.protected.json");
        var store = new KyraProviderCredentialStore(path);
        if (!store.IsProtectedLocalStorageAvailable)
        {
            return;
        }

        const string provider = "anthropic";
        const string secret = "fds-test-anthropic-secret";
        Assert.True(store.SaveSecret(provider, secret, out var status), status);

        Assert.DoesNotContain(secret, store.BuildSanitizedStatus(provider), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderConnectionTester_MissingKeyReturnsFriendlyStatus()
    {
        var provider = new FakeOnlineProvider(configured: false, result: null);
        var result = await new KyraProviderConnectionTester().TestAsync(
            provider,
            new CopilotProviderConfiguration(),
            new CopilotSettings(),
            "test");

        Assert.False(result.Ready);
        Assert.True(result.MissingCredential);
        Assert.Contains("Missing API key", result.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderConnectionTester_ConfiguredProviderUsesMinimalSafeRequest()
    {
        var provider = new FakeOnlineProvider(
            configured: true,
            result: new CopilotProviderResult { Succeeded = true, DiagnosticMessage = "ok" });

        var result = await new KyraProviderConnectionTester().TestAsync(
            provider,
            new CopilotProviderConfiguration { TimeoutSeconds = 3 },
            new CopilotSettings(),
            "test");

        Assert.True(result.Ready);
        Assert.Contains("minimal sanitized test request", result.UserMessage, StringComparison.Ordinal);
        Assert.Equal("Connection test. Reply with OK only.", provider.LastPrompt);
        Assert.Contains("No user files", provider.LastContextText, StringComparison.Ordinal);
    }

    private sealed class FakeOnlineProvider : ICopilotProvider
    {
        private readonly bool _configured;
        private readonly CopilotProviderResult? _result;

        public FakeOnlineProvider(bool configured, CopilotProviderResult? result)
        {
            _configured = configured;
            _result = result;
        }

        public string LastPrompt { get; private set; } = string.Empty;

        public string LastContextText { get; private set; } = string.Empty;

        public string Id => "fake-online";

        public string DisplayName => "Fake Online";

        public CopilotProviderType ProviderType => CopilotProviderType.OpenAICompatible;

        public string Category => "Test";

        public bool IsOnlineProvider => true;

        public bool IsPaidProvider => true;

        public bool EnabledByDefault => false;

        public string DefaultBaseUrl => "https://example.test/v1";

        public string DefaultModelName => "fake-model";

        public string DefaultApiKeyEnvironmentVariable => "FAKE_KEY";

        public string StatusText => "Test provider";

        public bool IsConfigured(CopilotProviderConfiguration configuration) => _configured;

        public bool CanHandle(CopilotProviderRequest request) => true;

        public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
        {
            LastPrompt = request.Prompt;
            LastContextText = request.Context.ContextText;
            return Task.FromResult(_result ?? new CopilotProviderResult
            {
                Succeeded = false,
                DiagnosticMessage = "fake failure"
            });
        }
    }
}
