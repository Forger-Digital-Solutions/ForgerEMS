using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Serialized xUnit collection for every test that mutates
/// <c>WineProbeGate.OverrideEnvironment</c> or relies on Wine-related
/// global state. Even though the override is now <see cref="System.Threading.AsyncLocal{T}"/>-backed
/// and cannot leak between parallel test contexts, marking these tests
/// as part of one non-parallel collection guarantees deterministic
/// execution order on flaky CI agents.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WineCompatibilityCollection
{
    public const string Name = "WineCompatibility";
}
