using System;

namespace MatjesUtils
{
    public static class ScanWaveforms
    {
        /// <summary>
        /// Normalized triangle wave (0 at phase 0, 1 at phase 0.5) with smoothed turnarounds.
        /// The waveform is the moving average of the ideal triangle over a window of
        /// 'turnaround' periods. This keeps the full scan velocity wherever the window does
        /// not contain a vertex and replaces each vertex with a constant-acceleration
        /// (parabolic) turnaround.
        ///   turnaround = 0   -> ideal triangle
        ///   turnaround = 1   -> constant 0.5
        /// </summary>
        /// <param name="phase">Normalized phase; any real value works, the wave has period 1.</param>
        /// <param name="turnaround">Smoothing window as a fraction of the period, 0..1.</param>
        public static double SmoothTriangle(double phase, double turnaround)
        {
            if (turnaround < 0.0 || turnaround > 1.0)
                throw new ArgumentOutOfRangeException(nameof(turnaround), "Must be between 0 and 1.");

            if (turnaround < 1e-6) return Triangle(phase);
            if (turnaround >= 1.0) return 0.5;

            double h = 0.5 * turnaround;
            return (TriangleIntegral(phase + h) - TriangleIntegral(phase - h)) / turnaround;
        }

        // Ideal triangle: 0 at phase 0, 1 at phase 0.5, back to 0 at phase 1.
        private static double Triangle(double x)
        {
            double p = x - Math.Floor(x);
            return p < 0.5 ? 2.0 * p : 2.0 - 2.0 * p;
        }

        // Antiderivative of Triangle, continuous across periods (each period adds 0.5).
        private static double TriangleIntegral(double x)
        {
            double n = Math.Floor(x);
            double p = x - n;
            double inCycle = p < 0.5 ? p * p : 2.0 * p - p * p - 0.5;
            return 0.5 * n + inCycle;
        }
    }
}
