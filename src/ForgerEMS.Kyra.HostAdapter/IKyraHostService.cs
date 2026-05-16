namespace ForgerEMS.Kyra.HostAdapter;

/// <summary>EMS-facing Kyra host API (maps to Kyra.Sdk in Phase 6c).</summary>
public interface IKyraHostService
{
    Task<KyraHostResponse> ProcessAsync(KyraHostRequest request, CancellationToken cancellationToken = default);
}
