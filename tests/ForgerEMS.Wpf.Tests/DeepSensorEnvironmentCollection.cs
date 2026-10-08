using Xunit;

namespace ForgerEMS.Wpf.Tests;

// Process-global Deep Sensor state: FORGEREMS_DEEP_SENSOR_MODE environment
// overrides and the LibreHardwareMonitor native probe lifecycle are not safe
// to run concurrently with the rest of the suite. DisableParallelization keeps
// this collection from running alongside any other collection.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DeepSensorEnvironmentCollection
{
    public const string Name = "DeepSensorEnvironment";
}
