using System.IO;

namespace VentoyToolkitSetup.Wpf.Services;

public static class DriverHubUrlSafety
{
    private static readonly string[] DeviceIdentifierQueryNames =
    {
        "auth",
        "auth_token",
        "servicetag",
        "service_tag",
        "serial",
        "serialnumber",
        "serial_number",
        "sn",
        "asset",
        "assettag",
        "asset_tag",
        "deviceid",
        "device_id",
        "email",
        "mail",
        "token",
        "access_token",
        "refresh_token",
        "apikey",
        "api_key",
        "license",
        "licensekey",
        "license_key"
    };

    private static readonly string[] TrackingQueryPrefixes =
    {
        "utm_"
    };

    private static readonly string[] TrackingQueryNames =
    {
        "fbclid",
        "gclid",
        "mc_cid",
        "mc_eid",
        "msclkid"
    };

    private static readonly string[] OfficialInstallerHosts =
    {
        "nvidia.com",
        "amd.com",
        "intel.com",
        "dell.com",
        "hp.com",
        "lenovo.com",
        "msi.com",
        "asus.com",
        "acer.com",
        "microsoft.com",
        "realtek.com",
        "gigabyte.com",
        "asrock.com"
    };

    private static readonly string[] InstallerExtensions =
    {
        ".exe",
        ".msi",
        ".msix",
        ".appx",
        ".zip"
    };

    public static bool IsSafeOfficialHttpUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !ContainsBlockedQuery(uri);
    }

    public static bool IsSafeOfficialInstallerDownloadUrl(string url)
    {
        if (!IsSafeOfficialHttpUrl(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!IsAllowedOfficialInstallerHost(uri.Host) ||
            !string.IsNullOrWhiteSpace(uri.Query) ||
            !string.IsNullOrWhiteSpace(uri.Fragment))
        {
            return false;
        }

        var extension = Path.GetExtension(uri.AbsolutePath);
        return InstallerExtensions.Any(allowed =>
            string.Equals(extension, allowed, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsBlockedQuery(Uri uri)
    {
        if (string.IsNullOrWhiteSpace(uri.Query))
        {
            return false;
        }

        var query = uri.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = part.Split('=', 2)[0];
            foreach (var blocked in DeviceIdentifierQueryNames)
            {
                if (string.Equals(name, blocked, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            foreach (var blocked in TrackingQueryNames)
            {
                if (string.Equals(name, blocked, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            foreach (var prefix in TrackingQueryPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsAllowedOfficialInstallerHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        return OfficialInstallerHosts.Any(allowed =>
            string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase));
    }
}
