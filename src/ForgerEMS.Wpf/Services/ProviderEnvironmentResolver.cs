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
        if (!string.IsNullOrWhiteSpace(session))
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

        var process = Environment.GetEnvironmentVariable(environmentVariableName, EnvironmentVariableTarget.Process);
        if (!string.IsNullOrWhiteSpace(process))
        {
            return new KyraCredentialResolution(process, KyraCredentialSource.ProcessEnvironment);
        }

        var user = Environment.GetEnvironmentVariable(environmentVariableName, EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(user))
        {
            return new KyraCredentialResolution(user, KyraCredentialSource.UserEnvironment);
        }

        var machine = Environment.GetEnvironmentVariable(environmentVariableName, EnvironmentVariableTarget.Machine);
        if (!string.IsNullOrWhiteSpace(machine))
        {
            return new KyraCredentialResolution(machine, KyraCredentialSource.MachineEnvironment);
        }

        return KyraCredentialResolution.Empty;
    }

    public static KyraCredentialResolution ResolveCloudflareAccountId()
        => ResolveFromEnvironmentVariable(CopilotProviderEnvironmentVariableNames.CloudflareAccountId);
}
