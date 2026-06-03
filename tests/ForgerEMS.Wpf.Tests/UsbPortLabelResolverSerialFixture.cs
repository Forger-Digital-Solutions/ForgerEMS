using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Serializes tests that mutate UsbPortLabelResolver's process-wide session state.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UsbPortLabelResolverSerialFixture
{
    public const string Name = "UsbPortLabelResolver";
}
