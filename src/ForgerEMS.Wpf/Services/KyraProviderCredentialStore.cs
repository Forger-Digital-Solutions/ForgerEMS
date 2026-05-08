using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

namespace VentoyToolkitSetup.Wpf.Services;

file static class CredentialStoreJsonOptions
{
    internal static readonly JsonSerializerOptions WriteIndented = new() { WriteIndented = true };
}

public interface IKyraCredentialStore
{
    bool IsProtectedLocalStorageAvailable { get; }

    bool HasSecret(string providerId);

    string TryGetSecret(string providerId);

    bool SaveSecret(string providerId, string secret, out string status);

    void ClearSecret(string providerId);

    string BuildSanitizedStatus(string providerId);
}

public sealed class KyraProviderCredentialStore : IKyraCredentialStore
{
    private static readonly object DefaultSync = new();
    private static IKyraCredentialStore? _default;

    public static IKyraCredentialStore Default
    {
        get
        {
            lock (DefaultSync)
            {
                return _default ??= new KyraProviderCredentialStore(DefaultCredentialPath());
            }
        }
    }

    public static void UseDefaultForTests(IKyraCredentialStore store)
    {
        lock (DefaultSync)
        {
            _default = store;
        }
    }

    public KyraProviderCredentialStore(string path)
    {
        _path = path;
    }

    private readonly string _path;
    private readonly object _sync = new();

    public bool IsProtectedLocalStorageAvailable
    {
        get
        {
            try
            {
                var payload = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes("probe"),
                    null,
                    DataProtectionScope.CurrentUser);
                var text = Encoding.UTF8.GetString(ProtectedData.Unprotect(payload, null, DataProtectionScope.CurrentUser));
                return text == "probe";
            }
            catch
            {
                return false;
            }
        }
    }

    public bool HasSecret(string providerId) =>
        Load().ContainsKey(NormalizeProviderId(providerId));

    public string TryGetSecret(string providerId)
    {
        lock (_sync)
        {
            var map = Load();
            if (!map.TryGetValue(NormalizeProviderId(providerId), out var protectedValue) ||
                string.IsNullOrWhiteSpace(protectedValue))
            {
                return string.Empty;
            }

            try
            {
                var bytes = Convert.FromBase64String(protectedValue);
                var plain = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public bool SaveSecret(string providerId, string secret, out string status)
    {
        status = string.Empty;
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(secret))
        {
            status = "No key supplied.";
            return false;
        }

        if (!IsProtectedLocalStorageAvailable)
        {
            status = "Protected local storage is unavailable; use session-only key instead.";
            return false;
        }

        lock (_sync)
        {
            try
            {
                var protectedBytes = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(secret.Trim()),
                    null,
                    DataProtectionScope.CurrentUser);
                var map = Load();
                map[NormalizeProviderId(providerId)] = Convert.ToBase64String(protectedBytes);
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(map, CredentialStoreJsonOptions.WriteIndented));
                status = "Saved as protected local key.";
                return true;
            }
            catch
            {
                status = "Could not save protected key; use session-only key instead.";
                return false;
            }
        }
    }

    public void ClearSecret(string providerId)
    {
        lock (_sync)
        {
            var map = Load();
            if (map.Remove(NormalizeProviderId(providerId)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(map, CredentialStoreJsonOptions.WriteIndented));
            }
        }
    }

    public string BuildSanitizedStatus(string providerId) =>
        HasSecret(providerId)
            ? "Protected local key present"
            : "No protected local key saved";

    private Dictionary<string, string> Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ??
                  new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string NormalizeProviderId(string providerId) =>
        string.IsNullOrWhiteSpace(providerId) ? "unknown" : providerId.Trim().ToLowerInvariant();

    private static string DefaultCredentialPath()
    {
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrWhiteSpace(local))
        {
            local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        return Path.Combine(local, "ForgerEMS", "Runtime", "config", "kyra-credentials.protected.json");
    }
}
