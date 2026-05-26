using System;
using System.Collections.Generic;
using System.Text.Json;

namespace VentoyToolkitSetup.Wpf.Services.Compatibility;

/// <summary>
/// Strongly-typed view of the JSON emitted by
/// <c>tools/linux/forgerems-linux-helper.sh</c>. Lives in the WPF project
/// so existing tests can exercise the parser without spawning the script.
/// </summary>
/// <remarks>
/// Parsing is lenient: any missing or malformed field becomes an empty
/// default. Failure to parse the document throws — callers wrap that in
/// a try/catch and treat it as "no Linux helper data available".
/// </remarks>
public sealed class LinuxHelperSnapshot
{
    public const string ExpectedSchema = "forgerems-linux-helper/1";

    public string Schema { get; init; } = string.Empty;

    public DateTimeOffset? GeneratedUtc { get; init; }

    public string DistroPrettyName { get; init; } = string.Empty;

    public string DistroId { get; init; } = string.Empty;

    public string DistroVersionId { get; init; } = string.Empty;

    public string Kernel { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, bool> ToolsAvailable { get; init; } = new Dictionary<string, bool>(StringComparer.Ordinal);

    public IReadOnlyList<LinuxHelperMount> Mounts { get; init; } = Array.Empty<LinuxHelperMount>();

    public IReadOnlyList<LinuxHelperBlockDevice> BlockDevices { get; init; } = Array.Empty<LinuxHelperBlockDevice>();

    public IReadOnlyList<LinuxHelperBlockDevice> RemovableDevices { get; init; } = Array.Empty<LinuxHelperBlockDevice>();

    public IReadOnlyList<LinuxHelperBlockDevice> VentoyPartitions { get; init; } = Array.Empty<LinuxHelperBlockDevice>();

    /// <summary>True when the document used the schema this parser was built for.</summary>
    public bool IsSchemaSupported => string.Equals(Schema, ExpectedSchema, StringComparison.Ordinal);

    public static LinuxHelperSnapshot Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Linux helper JSON was empty.", nameof(json));
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var distro = root.TryGetProperty("distro", out var distroEl) ? distroEl : default;
        return new LinuxHelperSnapshot
        {
            Schema = GetString(root, "schema"),
            GeneratedUtc = ParseTimestamp(GetString(root, "generated_utc")),
            DistroPrettyName = GetString(distro, "pretty_name"),
            DistroId = GetString(distro, "id"),
            DistroVersionId = GetString(distro, "version_id"),
            Kernel = GetString(root, "kernel"),
            ToolsAvailable = ParseTools(root),
            Mounts = ParseMounts(root),
            BlockDevices = ParseDevices(root, "block_devices"),
            RemovableDevices = ParseDevices(root, "removable_devices"),
            VentoyPartitions = ParseDevices(root, "ventoy_partitions")
        };
    }

    private static string GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (!element.TryGetProperty(property, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    }

    private static DateTimeOffset? ParseTimestamp(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(raw, out var parsed) ? parsed : null;
    }

    private static Dictionary<string, bool> ParseTools(JsonElement root)
    {
        var dict = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (!root.TryGetProperty("tools_available", out var tools) || tools.ValueKind != JsonValueKind.Object)
        {
            return dict;
        }

        foreach (var property in tools.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.True)
            {
                dict[property.Name] = true;
            }
            else if (property.Value.ValueKind == JsonValueKind.False)
            {
                dict[property.Name] = false;
            }
        }

        return dict;
    }

    private static IReadOnlyList<LinuxHelperMount> ParseMounts(JsonElement root)
    {
        if (!root.TryGetProperty("mounts", out var mounts) || mounts.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<LinuxHelperMount>();
        }

        var result = new List<LinuxHelperMount>(mounts.GetArrayLength());
        foreach (var entry in mounts.EnumerateArray())
        {
            result.Add(new LinuxHelperMount
            {
                Source = GetString(entry, "source"),
                Target = GetString(entry, "target"),
                FsType = GetString(entry, "fstype"),
                Options = GetString(entry, "options")
            });
        }

        return result;
    }

    private static IReadOnlyList<LinuxHelperBlockDevice> ParseDevices(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var devices) || devices.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<LinuxHelperBlockDevice>();
        }

        var result = new List<LinuxHelperBlockDevice>(devices.GetArrayLength());
        foreach (var entry in devices.EnumerateArray())
        {
            var removable = entry.TryGetProperty("removable", out var rm) && rm.ValueKind == JsonValueKind.True;
            result.Add(new LinuxHelperBlockDevice
            {
                Name = GetString(entry, "name"),
                Size = GetString(entry, "size"),
                Type = GetString(entry, "type"),
                Removable = removable,
                MountPoint = GetString(entry, "mountpoint"),
                Label = GetString(entry, "label"),
                Model = GetString(entry, "model"),
                Transport = GetString(entry, "transport")
            });
        }

        return result;
    }
}

public sealed class LinuxHelperMount
{
    public string Source { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string FsType { get; init; } = string.Empty;
    public string Options { get; init; } = string.Empty;
}

public sealed class LinuxHelperBlockDevice
{
    public string Name { get; init; } = string.Empty;
    public string Size { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public bool Removable { get; init; }
    public string MountPoint { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string Transport { get; init; } = string.Empty;
}
