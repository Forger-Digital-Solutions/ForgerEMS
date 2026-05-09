using System;
using System.Security.Principal;

namespace VentoyToolkitSetup.Wpf.Infrastructure;

public static class ProcessElevationHelper
{
    public static bool IsRunningElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static string DescribeElevationUiShort()
    {
        return IsRunningElevated()
            ? "Administrator (elevated)"
            : "Standard user (not elevated)";
    }
}
