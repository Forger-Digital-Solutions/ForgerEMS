using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ForgerEMS.Wpf.Services.Resources;

public sealed record ResourceCheckRequest
{
    /// <summary>Target architecture for USB media builds; normalized, never guessed.</summary>
    public string Architecture { get; init; } = "x64";

    /// <summary>Optional channel filter; defaults to the policy default.</summary>
    public string? Channel { get; init; }

    /// <summary>Optional subset of resource ids to check; null = all dynamic resources.</summary>
    public IReadOnlyList<string>? ResourceIds { get; init; }
}

/// <summary>
/// Runs resource descriptor checks: a per-resource semaphore deduplicates concurrent checks,
/// the policy metadata timeout bounds each descriptor, the TTL cache short-circuits fresh
/// entries, and every failure maps into a typed <see cref="ResourceResolution"/>.
/// </summary>
public sealed class ResourceCheckService
{
    private readonly ResourceProviderResolver _providers;
    private readonly ResourceMetadataCache _cache;
    private readonly ResourceCatalog _catalog;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public ResourceCheckService(
        ResourceCatalog catalog,
        ResourceProviderResolver? providers = null,
        ResourceMetadataCache? cache = null)
    {
        _catalog = catalog;
        _providers = providers ?? new ResourceProviderResolver(
            new OfficialMetadataClient(), catalog.MaximumAttempts);
        _cache = cache ?? new ResourceMetadataCache(DefaultCacheDirectory());
    }

    public ResourceCatalog Catalog => _catalog;

    /// <summary>Descriptors that require runtime metadata resolution (non-manual providers).</summary>
    public IReadOnlyList<ResourceDescriptor> DynamicResources =>
        _catalog.Resources.Where(r => r.Provider != ResourceProviderKind.OfficialPage).ToList();

    /// <summary>Descriptors that are explicit manual/vendor-page exceptions.</summary>
    public IReadOnlyList<ResourceDescriptor> ManualExceptions =>
        _catalog.Resources.Where(r => r.Provider == ResourceProviderKind.OfficialPage).ToList();

    public async Task<IReadOnlyList<ResourceResolution>> CheckAsync(
        ResourceCheckRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        request ??= new ResourceCheckRequest();
        var targetArch = ResourcePolicyValues.NormalizeArchitecture(request.Architecture);

        var descriptors = _catalog.Resources.AsEnumerable();
        if (request.ResourceIds is not null)
        {
            var wanted = new HashSet<string>(request.ResourceIds, StringComparer.Ordinal);
            descriptors = descriptors.Where(d => wanted.Contains(d.ResourceId));
        }

        var tasks = descriptors.Select(d =>
            CheckOneAsync(d, targetArch, request.Channel, cancellationToken));
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<ResourceResolution> CheckOneAsync(
        ResourceDescriptor descriptor,
        string targetArchitecture,
        string? requestedChannel,
        CancellationToken callerToken)
    {
        if (!string.Equals(descriptor.Architecture, targetArchitecture, StringComparison.OrdinalIgnoreCase))
        {
            return new ResourceResolution
            {
                Descriptor = descriptor,
                State = ResourceResolutionState.Unsupported,
                FailureCategory = ResourceFailureCategory.PolicyUnsupported,
                Reason = $"Descriptor targets '{descriptor.Architecture}', not requested '{targetArchitecture}'."
            };
        }

        if (!string.IsNullOrWhiteSpace(requestedChannel)
            && !string.Equals(descriptor.Channel, requestedChannel, StringComparison.OrdinalIgnoreCase))
        {
            return new ResourceResolution
            {
                Descriptor = descriptor,
                State = ResourceResolutionState.Unsupported,
                FailureCategory = ResourceFailureCategory.PolicyUnsupported,
                Reason = $"Descriptor channel '{descriptor.Channel}' does not match '{requestedChannel}'."
            };
        }

        if (descriptor.Provider == ResourceProviderKind.OfficialPage)
        {
            return await _providers.ResolveAsync(descriptor, callerToken).ConfigureAwait(false);
        }

        // Fresh cache hit short-circuits; stale is retained for display only.
        ResourceResolution? cached = null;
        var fresh = false;
        try
        {
            _cache.TryGet(descriptor, _catalog.CacheTtl, out cached, out fresh);
        }
        catch
        {
            cached = null;
        }

        if (fresh && cached is not null)
        {
            return cached;
        }

        // Per-resource semaphore: concurrent identical checks serialize without sharing a
        // caller's cancellation token across independent callers.
        var gate = _locks.GetOrAdd(
            ResourceMetadataCache.FingerprintFor(descriptor),
            _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(callerToken).ConfigureAwait(false);
        try
        {
            // Re-check the cache inside the gate: a serialized caller may find the entry
            // written by the caller that just released the lock.
            try
            {
                _cache.TryGet(descriptor, _catalog.CacheTtl, out var refreshed, out var stillFresh);
                if (stillFresh && refreshed is not null)
                {
                    return refreshed;
                }
            }
            catch
            {
                // keep going — cache failure must not break resolution
            }

            var resolution = await ResolveWithTimeoutAsync(descriptor, callerToken).ConfigureAwait(false);
            if (resolution.State == ResourceResolutionState.ResolvedMetadata)
            {
                // Stamp expiry on the fresh result before returning it.
                resolution = resolution with
                {
                    ExpiresAtUtc = resolution.CheckedAtUtc + _catalog.CacheTtl
                };

                try
                {
                    _cache.Store(resolution, _catalog.CacheTtl);
                }
                catch
                {
                    // cache persistence is best-effort
                }

                return resolution;
            }

            // Outage path: last-known-good stays visible as stale, never download-eligible.
            if (cached is not null)
            {
                return cached;
            }

            return resolution;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ResourceResolution> ResolveWithTimeoutAsync(
        ResourceDescriptor descriptor,
        CancellationToken callerToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeoutCts.CancelAfter(_catalog.MetadataTimeout);
        try
        {
            return await _providers.ResolveAsync(descriptor, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw; // caller cancellation propagates distinctly
        }
        catch (OperationCanceledException)
        {
            return new ResourceResolution
            {
                Descriptor = descriptor,
                State = ResourceResolutionState.UnableToVerify,
                FailureCategory = ResourceFailureCategory.Timeout,
                Reason = $"Metadata resolution exceeded {_catalog.MetadataTimeout.TotalSeconds}s."
            };
        }
    }

    internal static string DefaultCacheDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ForgerEMS", "resource-metadata-cache");
}
