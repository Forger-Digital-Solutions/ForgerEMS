namespace ForgerEMS.Kyra.HostAdapter;

/// <summary>EMS environment variable names for optional SDK dogfood gateway (read-only; never persisted).</summary>
public static class KyraSdkDogfoodEnvironment
{
    public const string GatewayUrlVariable = "FORGEREMS_KYRA_GATEWAY_URL";

    public const string GatewayBetaTokenVariable = "FORGEREMS_KYRA_GATEWAY_BETA_TOKEN";

    public static string? ReadGatewayUrl() =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GatewayUrlVariable))
            ? null
            : Environment.GetEnvironmentVariable(GatewayUrlVariable)!.Trim();

    public static string? ReadGatewayBetaToken() =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GatewayBetaTokenVariable))
            ? null
            : Environment.GetEnvironmentVariable(GatewayBetaTokenVariable);
}
