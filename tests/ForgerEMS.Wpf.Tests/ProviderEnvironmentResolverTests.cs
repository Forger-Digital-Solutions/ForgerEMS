using System;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class ProviderEnvironmentResolverTests
{
    private sealed class FakeProvider : ICopilotProvider
    {
        public FakeProvider(
            string id,
            string displayName,
            bool online,
            string statusText,
            string defaultEnv,
            Func<CopilotProviderConfiguration, bool> isConfigured)
        {
            Id = id;
            DisplayName = displayName;
            ProviderType = CopilotProviderType.GroqApi;
            Category = "Free API pool";
            IsOnlineProvider = online;
            IsPaidProvider = false;
            EnabledByDefault = false;
            DefaultBaseUrl = "https://example.invalid";
            DefaultModelName = "test-model";
            DefaultApiKeyEnvironmentVariable = defaultEnv;
            StatusText = statusText;
            _isConfigured = isConfigured;
        }

        private readonly Func<CopilotProviderConfiguration, bool> _isConfigured;

        public string Id { get; }
        public string DisplayName { get; }
        public CopilotProviderType ProviderType { get; }
        public string Category { get; }
        public bool IsOnlineProvider { get; }
        public bool IsPaidProvider { get; }
        public bool EnabledByDefault { get; }
        public string DefaultBaseUrl { get; }
        public string DefaultModelName { get; }
        public string DefaultApiKeyEnvironmentVariable { get; }
        public string StatusText { get; }

        public bool IsConfigured(CopilotProviderConfiguration configuration) => _isConfigured(configuration);

        public bool CanHandle(CopilotProviderRequest request) => true;

        public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new CopilotProviderResult());
    }

    private static string UniqueName(string prefix)
        => prefix + Guid.NewGuid().ToString("N");

    [Fact]
    public void ProcessEnvironment_IsDetected()
    {
        var name = UniqueName("FORGEREMS_UT_PROC_");
        try
        {
            Environment.SetEnvironmentVariable(name, "proc-value", EnvironmentVariableTarget.Process);
            var r = ProviderEnvironmentResolver.ResolveFromEnvironmentVariable(name);
            Assert.Equal(KyraCredentialSource.ProcessEnvironment, r.Source);
            Assert.Equal("proc-value", r.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void UserEnvironment_IsDetectedWhenProcessEmpty()
    {
        var name = UniqueName("FORGEREMS_UT_USER_");
        try
        {
            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(name, "user-value", EnvironmentVariableTarget.User);
            var r = ProviderEnvironmentResolver.ResolveFromEnvironmentVariable(name);
            Assert.Equal(KyraCredentialSource.UserEnvironment, r.Source);
            Assert.Equal("user-value", r.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.User);
        }
    }

    [Fact]
    public void SessionKey_OverridesEnvironment()
    {
        var name = UniqueName("FORGEREMS_UT_SESS_");
        var providerId = "test-provider-" + Guid.NewGuid().ToString("N");
        try
        {
            Environment.SetEnvironmentVariable(name, "from-env", EnvironmentVariableTarget.Process);
            KyraApiKeyStore.SetSessionKey(providerId, "from-session");
            var r = ProviderEnvironmentResolver.ResolveApiCredential(providerId, name);
            Assert.Equal(KyraCredentialSource.Session, r.Source);
            Assert.Equal("from-session", r.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
            KyraApiKeyStore.ClearSessionKey(providerId);
        }
    }

    [Fact]
    public void MissingKey_ShowsNotConfigured()
    {
        var name = UniqueName("FORGEREMS_UT_NONE_");
        var r = ProviderEnvironmentResolver.ResolveFromEnvironmentVariable(name);
        Assert.Equal(KyraCredentialSource.None, r.Source);
        Assert.Null(r.Value);
        Assert.Contains("Not configured", r.DescribeUx(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescribeUx_NeverContainsSecretValue()
    {
        var name = UniqueName("FORGEREMS_UT_MASK_");
        try
        {
            var secret = "super-secret-key-do-not-leak-12345";
            Environment.SetEnvironmentVariable(name, secret, EnvironmentVariableTarget.Process);
            var r = ProviderEnvironmentResolver.ResolveFromEnvironmentVariable(name);
            Assert.DoesNotContain(secret, r.DescribeUx(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void CredentialSourceLine_ReflectsRefreshAfterProcessEnvChange()
    {
        var name = UniqueName("FORGEREMS_UT_REF_");
        var provider = new FakeProvider(
            "groq-free",
            "Groq",
            online: true,
            "Live API",
            name,
            _ => true);
        var cfg = new CopilotProviderConfiguration
        {
            IsEnabled = true,
            ApiKeyEnvironmentVariable = name
        };

        try
        {
            var before = CopilotProviderStatusFormatter.BuildCredentialSourceLine(provider, cfg);
            Assert.Contains("not configured", before, StringComparison.OrdinalIgnoreCase);

            Environment.SetEnvironmentVariable(name, "k", EnvironmentVariableTarget.Process);
            var after = CopilotProviderStatusFormatter.BuildCredentialSourceLine(provider, cfg);
            Assert.Contains("process env", after, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
        }
    }
}
