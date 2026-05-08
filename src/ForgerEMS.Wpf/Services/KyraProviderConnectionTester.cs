#pragma warning disable CA1822 // DI-injected service; method called via instance reference
namespace VentoyToolkitSetup.Wpf.Services;

public sealed class KyraProviderConnectionTester
{
    public async Task<KyraProviderConnectionTestResult> TestAsync(
        ICopilotProvider provider,
        CopilotProviderConfiguration configuration,
        CopilotSettings settings,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(settings);

        if (!provider.IsOnlineProvider)
        {
            return KyraProviderConnectionTestResult.Success("Ready. Local provider path is available when its local service is running.");
        }

        if (!provider.IsConfigured(configuration))
        {
            return KyraProviderConnectionTestResult.MissingKey("Missing API key. Add a session key, protected saved key, or environment variable.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(configuration.TimeoutSeconds <= 0 ? 8 : configuration.TimeoutSeconds, 3, 12)));

        try
        {
            var request = new CopilotProviderRequest
            {
                AppVersion = appVersion,
                Prompt = "Connection test. Reply with OK only.",
                Settings = settings,
                ProviderConfiguration = configuration,
                Context = new CopilotContext
                {
                    UserQuestion = "Connection test. Reply with OK only.",
                    ContextText = "Provider connection test. No user files, logs, serials, or private paths included.",
                    Intent = KyraIntent.GeneralTechQuestion,
                    PersonalityProfile = settings.PersonalityProfile
                }
            };

            var result = await provider.GenerateAsync(request, timeout.Token).ConfigureAwait(false);
            if (result.Succeeded)
            {
                return KyraProviderConnectionTestResult.Success("Ready. Provider responded to a minimal sanitized test request.");
            }

            var detail = SensitiveDataRedactor.SanitizeForSupportShare(result.DiagnosticMessage);
            return KyraProviderConnectionTestResult.Error(string.IsNullOrWhiteSpace(detail)
                ? "Provider test failed with a sanitized provider error."
                : $"Provider test failed: {detail}");
        }
        catch (OperationCanceledException)
        {
            return KyraProviderConnectionTestResult.Error("Provider test timed out. Check network, base URL, model, and key.");
        }
        catch (Exception exception)
        {
            var detail = SensitiveDataRedactor.SanitizeForSupportShare(exception.Message);
            return KyraProviderConnectionTestResult.Error(string.IsNullOrWhiteSpace(detail)
                ? "Provider test failed with a sanitized error."
                : $"Provider test failed: {detail}");
        }
    }
}

public readonly record struct KyraProviderConnectionTestResult(
    bool Ready,
    bool MissingCredential,
    string UserMessage)
{
    public static KyraProviderConnectionTestResult Success(string message) => new(true, false, message);

    public static KyraProviderConnectionTestResult MissingKey(string message) => new(false, true, message);

    public static KyraProviderConnectionTestResult Error(string message) => new(false, false, message);
}
