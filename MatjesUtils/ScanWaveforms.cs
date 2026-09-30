using System;

namespace MatjesUtils
{
    public static class ScanWaveforms
    {
        /// <summary>
        /// Normalized triangle wave (0 at phase 0, 1 at phase 0.5) with smoothed turnarounds.
        /// Equivalent to SmoothSawtooth(phase, 0.5, turnaround).
        ///   turnaround = 0   -> ideal triangle
        ///   turnaround = 1   -> constant 0.5
        /// </summary>
        /// <param name="phase">Normalized phase; any real value works, the wave has period 1.</param>
        /// <param name="turnaround">Smoothing window as a fraction of the period, 0..1.</param>
        public static double SmoothTriangle(double phase, double turnaround)
        {
            return SmoothSawtooth(phase, 0.5, turnaround);
        }

        /// <summary>
        /// Normalized asymmetric triangle / sawtooth wave between 0 and 1 with period 1.
        /// The wave rises from 0 at phase 0 to 1 at phase = shape, then falls back to 0 at phase 1.
        ///   shape = 1.0 -> slow rise, instant fall
        ///   shape = 0.5 -> symmetric triangle
        ///   shape = 0.0 -> instant rise, slow fall
        /// </summary>
        /// <param name="phase">Normalized phase; any real value works, the wave has period 1.</param>
        /// <param name="shape">Fraction of the period spent rising, 0..1.</param>
        public static double Sawtooth(double phase, double shape)
        {
            ValidateUnit(shape, nameof(shape));

            double p = phase - Math.Floor(phase);
            return p < shape ? p / shape : (1.0 - p) / (1.0 - shape);
        }

        /// <summary>
        /// Sawtooth (see <see cref="Sawtooth"/>) with smoothed turnarounds. The waveform is the
        /// moving average of the ideal sawtooth over a window of 'turnaround' periods. Where the
        /// window does not contain a vertex, the original rise and fall velocities are kept;
        /// each vertex is replaced by a constant-acceleration (parabolic) turnaround.
        ///   turnaround = 0   -> ideal sawtooth
        ///   turnaround = 1   -> constant 0.5
        /// </summary>
        /// <param name="phase">Normalized phase; any real value works, the wave has period 1.</param>
        /// <param name="shape">Fraction of the period spent rising, 0..1.</param>
        /// <param name="turnaround">Smoothing window as a fraction of the period, 0..1.</param>
        public static double SmoothSawtooth(double phase, double shape, double turnaround)
        {
            ValidateUnit(shape, nameof(shape));
            ValidateUnit(turnaround, nameof(turnaround));

            if (turnaround < 1e-6) return Sawtooth(phase, shape);
            if (turnaround >= 1.0) return 0.5;

            double h = 0.5 * turnaround;
            return (SawtoothIntegral(phase + h, shape) - SawtoothIntegral(phase - h, shape)) / turnaround;
        }

        // Antiderivative of Sawtooth, continuous across periods (each period adds 0.5).
        private static double SawtoothIntegral(double x, double shape)
        {
            double n = Math.Floor(x);
            double p = x - n;
            double inCycle = p < shape
                ? p * p / (2.0 * shape)
                : 0.5 - (1.0 - p) * (1.0 - p) / (2.0 * (1.0 - shape));
            return 0.5 * n + inCycle;
        }

        private static void ValidateUnit(double value, string name)
        {
            if (value < 0.0 || value > 1.0)
                throw new ArgumentOutOfRangeException(name, "Must be between 0 and 1.");
        }
    }
}
