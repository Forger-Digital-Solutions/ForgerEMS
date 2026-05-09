using Xunit;

namespace ForgerEMS.Wpf.Tests;

// Serializes test classes that mutate process-level gateway env vars so they cannot race.
// Any class that sets FORGEREMS_KYRA_GATEWAY_URL / BETA_TOKEN / ENABLED in its tests must
// join this collection.
[CollectionDefinition("GatewayEnv", DisableParallelization = true)]
public sealed class GatewayEnvSequential { }
