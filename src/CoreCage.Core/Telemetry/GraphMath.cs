using System;

namespace CoreCage.Core.Telemetry
{
    /// <summary>Pure geometry mapping for live line graphs: chronological samples -> (x,y) points
    /// across a width x height area. X spreads oldest(0)->newest(width). Y is inverted so a higher
    /// value sits higher on screen (value>=max -> y=0 top; value<=min -> y=height bottom). When
    /// max<=min the series is drawn as a flat mid-line. Fewer than 2 samples -> empty. No WPF dep.</summary>
    public static class GraphMath
    {
        public static (double X, double Y)[] BuildPoints(double[] samples, double width, double height, double min, double max)
        {
            if (samples == null || samples.Length < 2) return Array.Empty<(double, double)>();
            int n = samples.Length;
            var pts = new (double X, double Y)[n];
            double range = max - min;
            bool flat = range <= 0;
            for (int i = 0; i < n; i++)
            {
                double x = width * i / (n - 1);
                double y;
                if (flat) y = height / 2.0;
                else
                {
                    double t = (samples[i] - min) / range;
                    if (double.IsNaN(t) || t < 0) t = 0; else if (t > 1) t = 1;
                    y = height * (1 - t);
                }
                pts[i] = (x, y);
            }
            return pts;
        }
    }
}
