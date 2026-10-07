using Xunit;

namespace ForgerEMS.Wpf.Tests;

// Wall-clock process checks need an idle test runner, not competing PowerShell test collections.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ReleaseValidatorPerformanceCollection
{
    public const string Name = "ReleaseValidatorPerformance";
}
