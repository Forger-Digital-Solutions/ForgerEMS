using System;

namespace VentoyToolkitSetup.Wpf.Services;

public enum KyraCredentialSource
{
    None,
    Session,
    ProcessEnvironment,
    UserEnvironment,
    MachineEnvironment
}

public readonly struct KyraCredentialResolution
{
    public KyraCredentialResolution(string? value, KyraCredentialSource source)
    {
        Value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        Source = Value is null ? KyraCredentialSource.None : source;
    }

    public string? Value { get; }

    public KyraCredentialSource Source { get; }

    public static KyraCredentialResolution Empty => new(null, KyraCredentialSource.None);

    public string DescribeUx()
    {
        return Source switch
        {
            KyraCredentialSource.Session => "Configured via session key",
            KyraCredentialSource.ProcessEnvironment => "Configured via process env",
            KyraCredentialSource.UserEnvironment => "Configured via user env",
            KyraCredentialSource.MachineEnvironment => "Configured via machine env",
            KyraCredentialSource.None => "Not configured",
            _ => "Not configured"
        };
    }
}

/// <summary>
/// Resolves provider secrets from session (highest priority) then process, user, and machine environment blocks.
/// Never logs or persists raw values.
/// </summary>
public static class ProviderEnvironmentResolver
{
    public static KyraCredentialResolution ResolveApiCredential(string providerId, string? apiKeyEnvironmentVariable)
    {
        var session = KyraApiKeyStore.GetSessionKey(providerId);
        if (!KyraProviderConfigResolver.IsMissingOrPlaceholder(session))
        {
            return new KyraCredentialResolution(session, KyraCredentialSource.Session);
        }

        if (string.IsNullOrWhiteSpace(apiKeyEnvironmentVariable))
        {
            return KyraCredentialResolution.Empty;
        }

        return ResolveFromEnvironmentVariable(apiKeyEnvironmentVariable);
    }

    public static KyraCredentialResolution ResolveFromEnvironmentVariable(string environmentVariableName)
    {
        if (string.IsNullOrWhiteSpace(environmentVariableName))
        {
            return KyraCredentialResolution.Empty;
        }

        var process = SafeGetEnvironmentVariable(environmentVariableName, EnvironmentVariableTarget.Process);
        if (!KyraProviderConfigResolver.IsMissingOrPlaceholder(process))
        {
            return new KyraCredentialResolution(process, KyraCredentialSource.ProcessEnvironment);
        }

        var user = SafeGetEnvironmentVariable(environmentVariableName, EnvironmentVariableTarget.User);
        if (!KyraProviderConfigResolver.IsMissingOrPlaceholder(user))
        {
            return new KyraCredentialResolution(user, KyraCredentialSource.UserEnvironment);
        }

        var machine = SafeGetEnvironmentVariable(environmentVariableName, EnvironmentVariableTarget.Machine);
        if (!KyraProviderConfigResolver.IsMissingOrPlaceholder(machine))
        {
            return new KyraCredentialResolution(machine, KyraCredentialSource.MachineEnvironment);
        }

        return KyraCredentialResolution.Empty;
    }

    public static KyraCredentialResolution ResolveCloudflareAccountId()
        => ResolveFromEnvironmentVariable(CopilotProviderEnvironmentVariableNames.CloudflareAccountId);

    private static string? SafeGetEnvironmentVariable(string name, EnvironmentVariableTarget target)
    {
        try
        {
            return Environment.GetEnvironmentVariable(name, target);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
