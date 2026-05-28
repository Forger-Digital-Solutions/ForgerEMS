namespace VentoyToolkitSetup.Wpf.Services;

public static class DriverHubUrlSafety
{
    private static readonly string[] DeviceIdentifierQueryNames =
    {
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
        "device_id"
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

        return !ContainsDeviceIdentifierQuery(uri);
    }

    private static bool ContainsDeviceIdentifierQuery(Uri uri)
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
        }

        return false;
    }
}
