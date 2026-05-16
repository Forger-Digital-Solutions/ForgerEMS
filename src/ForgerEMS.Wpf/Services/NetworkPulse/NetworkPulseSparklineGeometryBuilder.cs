using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

public static class NetworkPulseSparklineGeometryBuilder
{
    public static Geometry? BuildPingSparkline(IReadOnlyList<double> normalized, double width, double height)
    {
        if (normalized.Count < 2 || width <= 1 || height <= 1)
        {
            return null;
        }

        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            var step = width / Math.Max(1, normalized.Count - 1);
            for (var i = 0; i < normalized.Count; i++)
            {
                var x = i * step;
                var y = height - (normalized[i] * (height - 2)) - 1;
                if (i == 0)
                {
                    ctx.BeginFigure(new Point(x, y), isFilled: false, isClosed: false);
                }
                else
                {
                    ctx.LineTo(new Point(x, y), isStroked: true, isSmoothJoin: false);
                }
            }
        }

        g.Freeze();
        return g;
    }
}
