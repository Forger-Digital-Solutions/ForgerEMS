using System.Net;

namespace VentoyToolkitSetup.Wpf.Services;

public static class UsbHtmlEscaper
{
    public static string Escape(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    public static string EscapeAttribute(string? value) => Escape(value).Replace("\"", "&quot;", StringComparison.Ordinal);
}
