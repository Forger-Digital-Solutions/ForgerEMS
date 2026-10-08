using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Models;

namespace ForgerEMS.Wpf.Services.Resources;

/// <summary>
/// Loads and validates the lead-authored manifests/resource-policy.json. Validation is strict:
/// schema version, unique ids, HTTPS sources, known providers, explicit architecture.
/// </summary>
public sealed record ResourceCatalog
{
    public const int SupportedSchemaVersion = 1;

    public required string SourcePath { get; init; }
    public required int SchemaVersion { get; init; }
    public required string DefaultChannel { get; init; }
    public required TimeSpan MetadataTimeout { get; init; }
    public required int MaximumAttempts { get; init; }
    public required TimeSpan CacheTtl { get; init; }
    public required IReadOnlyList<ResourceDescriptor> Resources { get; init; }

    public bool DownloadRequiresExpectedSha256 { get; init; } = true;
    public bool StaleMetadataPermitsInstallation { get; init; }
    public bool MissingProviderPermitsPinnedFallback { get; init; }

    public ResourceDescriptor? Find(string resourceId) =>
        Resources.FirstOrDefault(r => string.Equals(r.ResourceId, resourceId, StringComparison.Ordinal));

    public static ResourceCatalog Load(string path)
    {
        var json = File.ReadAllText(path);
        var catalog = Parse(json);
        return catalog with { SourcePath = path };
    }

    /// <summary>Locate the policy next to the backend manifests, preferring the repository copy.</summary>
    public static string? FindPolicyPath(BackendContext backendContext)
    {
        var candidates = new List<string?>
        {
            backendContext.RepoManifestPath?.Replace("ForgerEMS.updates.json", "resource-policy.json"),
            backendContext.PrimaryManifestPath?.Replace("ForgerEMS.updates.json", "resource-policy.json"),
            backendContext.RootPath is null ? null : Path.Combine(backendContext.RootPath, "manifests", "resource-policy.json"),
            backendContext.WorkingDirectory is null ? null : Path.Combine(backendContext.WorkingDirectory, "manifests", "resource-policy.json")
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                {
                    return full;
                }
            }
            catch
            {
                // malformed candidate path — keep looking
            }
        }

        return null;
    }

    public static ResourceCatalog Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false });
        }
        catch (JsonException ex)
        {
            throw new ResourceCatalogException($"resource-policy.json is not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ResourceCatalogException("resource-policy.json root must be an object.");
            }

            var schemaVersion = RequiredInt(root, "schemaVersion");
            if (schemaVersion != SupportedSchemaVersion)
            {
                throw new ResourceCatalogException($"Unsupported resource policy schemaVersion {schemaVersion}; expected {SupportedSchemaVersion}.");
            }

            var defaultChannel = RequiredString(root, "defaultChannel");
            var timeoutSeconds = BoundedInt(root, "metadataTimeoutSeconds", 20, 1, 300);
            var maxAttempts = BoundedInt(root, "maximumAttempts", 2, 1, 5);
            var cacheTtlMinutes = BoundedInt(root, "cacheTtlMinutes", 60, 1, 1440);

            bool downloadRequiresSha = true, stalePermits = false, missingProviderPermitsFallback = false;
            if (root.TryGetProperty("policy", out var policy) && policy.ValueKind == JsonValueKind.Object)
            {
                downloadRequiresSha = OptionalBool(policy, "downloadRequiresExpectedSha256", true);
                stalePermits = OptionalBool(policy, "staleMetadataPermitsInstallation", false);
                missingProviderPermitsFallback = OptionalBool(policy, "missingProviderPermitsPinnedFallback", false);
            }

            if (!root.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
            {
                throw new ResourceCatalogException("resource-policy.json must contain a resources array.");
            }

            var descriptors = new List<ResourceDescriptor>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in resources.EnumerateArray())
            {
                var descriptor = ParseDescriptor(element);
                if (!seen.Add(descriptor.ResourceId))
                {
                    throw new ResourceCatalogException($"Duplicate resource id '{descriptor.ResourceId}'.");
                }

                descriptors.Add(descriptor);
            }

            return new ResourceCatalog
            {
                SourcePath = string.Empty,
                SchemaVersion = schemaVersion,
                DefaultChannel = defaultChannel,
                MetadataTimeout = TimeSpan.FromSeconds(timeoutSeconds),
                MaximumAttempts = maxAttempts,
                CacheTtl = TimeSpan.FromMinutes(cacheTtlMinutes),
                Resources = descriptors,
                DownloadRequiresExpectedSha256 = downloadRequiresSha,
                StaleMetadataPermitsInstallation = stalePermits,
                MissingProviderPermitsPinnedFallback = missingProviderPermitsFallback
            };
        }
    }

    private static ResourceDescriptor ParseDescriptor(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ResourceCatalogException("Each resource entry must be an object.");
        }

        var id = RequiredString(element, "id");
        var name = RequiredString(element, "name");
        var providerText = RequiredString(element, "provider");
        var source = RequiredString(element, "source");
        var architecture = RequiredString(element, "architecture");

        var provider = providerText switch
        {
            "github-stable" => ResourceProviderKind.GitHubStable,
            "checksum-index" => ResourceProviderKind.ChecksumIndex,
            "ubuntu-lts" => ResourceProviderKind.UbuntuLts,
            "official-page" => ResourceProviderKind.OfficialPage,
            var other => throw new ResourceCatalogException($"Resource '{id}' has unknown provider '{other}'.")
        };

        if (!Uri.TryCreate(source, UriKind.Absolute, out var sourceUri)
            || !string.Equals(sourceUri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            throw new ResourceCatalogException($"Resource '{id}' source must be an absolute HTTPS URL.");
        }

        var normalizedArch = ResourcePolicyValues.NormalizeArchitecture(architecture);
        if (!ResourcePolicyValues.IsSupportedArchitecture(normalizedArch))
        {
            throw new ResourceCatalogException($"Resource '{id}' has unsupported architecture '{architecture}'.");
        }

        var allowedHosts = new List<string>();
        if (element.TryGetProperty("allowedHosts", out var hosts) && hosts.ValueKind == JsonValueKind.Array)
        {
            foreach (var host in hosts.EnumerateArray())
            {
                var value = host.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    allowedHosts.Add(value!.Trim().ToLowerInvariant());
                }
            }
        }

        var baseUrl = OptionalString(element, "baseUrl");
        if (baseUrl is not null
            && (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
                || !string.Equals(baseUri.Scheme, "https", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ResourceCatalogException($"Resource '{id}' baseUrl must be an absolute HTTPS URL.");
        }

        var repository = OptionalString(element, "repository");
        if (provider == ResourceProviderKind.GitHubStable
            && (string.IsNullOrWhiteSpace(repository) || !repository!.Contains('/')))
        {
            throw new ResourceCatalogException($"GitHub resource '{id}' must declare an owner/repo repository.");
        }

        var channel = OptionalString(element, "channel");
        var role = OptionalString(element, "role");

        return new ResourceDescriptor
        {
            ResourceId = id,
            DisplayName = name,
            Provider = provider,
            SourceUri = source,
            Repository = repository,
            AssetPattern = OptionalString(element, "assetPattern"),
            BaseUrl = baseUrl,
            Architecture = normalizedArch,
            Platform = OptionalString(element, "platform") ?? string.Empty,
            Channel = channel ?? ResourcePolicyValues.StableChannel,
            Role = role,
            AllowedHosts = allowedHosts,
            ExceptionReason = OptionalString(element, "exception")
        };
    }

    private static string RequiredString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new ResourceCatalogException($"Missing required string property '{name}'.");
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ResourceCatalogException($"Property '{name}' must not be empty.");
        }

        return text!;
    }

    private static string? OptionalString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int RequiredInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
        {
            throw new ResourceCatalogException($"Missing required integer property '{name}'.");
        }

        return result;
    }

    private static int OptionalInt(JsonElement obj, string name, int fallback) =>
        obj.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var result) ? result : fallback;

    private static int BoundedInt(JsonElement obj, string name, int fallback, int min, int max)
    {
        if (!obj.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw new ResourceCatalogException($"Policy '{name}' must be an integer.");
        }

        if (result < min || result > max)
        {
            throw new ResourceCatalogException($"Policy '{name}' must be between {min} and {max}; got {result}.");
        }

        return result;
    }

    private static bool OptionalBool(JsonElement obj, string name, bool fallback) =>
        obj.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : fallback;
}

public sealed class ResourceCatalogException : Exception
{
    public ResourceCatalogException(string message) : base(message)
    {
    }
}
