using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MatjesUtils
{
    /// <summary>
    /// Result of the piezo linearity analysis of a volume scan test
    /// </summary>
    public class PiezoLinearityResult
    {
        /// <summary>
        /// False if no usable linear window was found. Message says why
        /// </summary>
        public bool IsValid { get; init; }

        /// <summary>
        /// Summary or reason why the analysis failed
        /// </summary>
        public string Message { get; init; } = "";

        /// <summary>
        /// The number of volumes (sawtooth cycles) that were analyzed
        /// </summary>
        public int CyclesAnalyzed { get; init; }

        /// <summary>
        /// The number of cycles in which the linear window reached the edge of the search range,
        /// i.e. the reported window is a safe underestimate on those cycles
        /// </summary>
        public int CyclesBoundaryLimited { get; init; }

        /// <summary>
        /// Start of the linear window common to all cycles, in samples from volume start
        /// </summary>
        public int WindowStartSample { get; init; }

        /// <summary>
        /// End (inclusive) of the linear window common to all cycles, in samples from volume start
        /// </summary>
        public int WindowEndSample { get; init; }

        /// <summary>
        /// The steady-state piezo velocity on the imaging flank in microns per sample (median across cycles)
        /// </summary>
        public double VelocityMicronsPerSample { get; init; }

        /// <summary>
        /// The first frame (plane index within the volume) that lies entirely inside the linear window, -1 if none
        /// </summary>
        public int FirstLinearPlane { get; init; } = -1;

        /// <summary>
        /// The last frame (plane index within the volume) that lies entirely inside the linear window, -1 if none
        /// </summary>
        public int LastLinearPlane { get; init; } = -1;

        /// <summary>
        /// The number of frames inside the linear window
        /// </summary>
        public int NumLinearPlanes => FirstLinearPlane < 0 ? 0 : LastLinearPlane - FirstLinearPlane + 1;

        /// <summary>
        /// For each frame of the volume the read-back piezo position in microns, averaged over the frame's
        /// exposure and across cycles
        /// </summary>
        public double[] PlanePositionsMicrons { get; init; } = [];

        /// <summary>
        /// The command waveform of one volume in microns, averaged across cycles
        /// </summary>
        public double[] CycleAverageCommand { get; init; } = [];

        /// <summary>
        /// The read-back waveform of one volume in microns, averaged across cycles
        /// </summary>
        public double[] CycleAverageReadback { get; init; } = [];
    }

    /// <summary>
    /// Finds the part of each piezo sawtooth cycle in which the piezo moves at constant velocity, from the
    /// position read-back recorded during a volume scan. Port of the analysis in the NI DAQmx sawtooth test script
    /// (analyze_linear_window / find_linear_window / count_usable_frames) adapted to the imaging flank used by
    /// ScanControl, which is the rising flank of the sawtooth.
    /// The piezo low-pass filters its command, so its read-back is curved after each direction reversal and only
    /// straight in between. Only frames exposed during the straight part have a uniform z-step.
    /// All indices are in read-back samples. Sample n of the read-back has to be aligned with sample n of the command,
    /// which holds when both are triggered by the same counter edge and clocked at the same rate.
    /// </summary>
    public static class PiezoLinearity
    {
        /// <summary>
        /// Maximum relative deviation from the reference velocity still counted as linear (0.05 = 5%)
        /// </summary>
        public const double DefaultVelocityTolerance = 0.05;

        /// <summary>
        /// Central fraction of the command's imaging flank used to estimate the reference (steady-state) velocity
        /// </summary>
        public const double CenterFraction = 0.5;

        /// <summary>
        /// The search window around each imaging flank is widened by this fraction of the volume period on each side
        /// (clipped to the neighboring flanks) so that the search can find where linearity breaks down
        /// </summary>
        public const double PadFraction = 0.3;

        /// <summary>
        /// Analyzes the piezo read-back of a volume scan
        /// </summary>
        /// <param name="commandMicrons">Piezo command per sample in microns</param>
        /// <param name="readbackMicrons">Piezo read-back per sample in microns, aligned with the command</param>
        /// <param name="samplesPerVolume">The number of samples in one volume (sawtooth period)</param>
        /// <param name="samplesPerFrame">The number of samples in one camera frame. Has to divide samplesPerVolume</param>
        /// <param name="sawtoothShape">Fraction of the period spent on the rising (imaging) flank</param>
        /// <param name="skipCycles">The number of initial cycles to ignore while the piezo settles into periodic motion</param>
        /// <param name="velocityTolerance">Relative velocity deviation still counted as linear</param>
        /// <param name="smoothSamples">Samples of the local fit used to estimate velocity from the noisy read-back.
        /// Defaults to one frame, which keeps the velocity estimate stable while each frame averages the position over its exposure anyway</param>
        public static PiezoLinearityResult Analyze(double[] commandMicrons, double[] readbackMicrons, int samplesPerVolume, int samplesPerFrame,
            double sawtoothShape, int skipCycles = 1, double velocityTolerance = DefaultVelocityTolerance, int? smoothSamples = null)
        {
            ArgumentNullException.ThrowIfNull(commandMicrons);
            ArgumentNullException.ThrowIfNull(readbackMicrons);
            if (commandMicrons.Length != readbackMicrons.Length)
                throw new ArgumentException("Command and read-back need to have the same length.");
            ArgumentOutOfRangeException.ThrowIfLessThan(samplesPerFrame, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(samplesPerVolume, samplesPerFrame);
            if (samplesPerVolume % samplesPerFrame != 0)
                throw new ArgumentException("Samples per volume has to be a multiple of samples per frame.");
            if (sawtoothShape <= 0 || sawtoothShape > 1)
                throw new ArgumentOutOfRangeException(nameof(sawtoothShape), "Sawtooth shape has to be in (0, 1].");
            ArgumentOutOfRangeException.ThrowIfNegative(skipCycles);

            int smooth = Math.Max(5, smoothSamples ?? samplesPerFrame) | 1;
            int period = samplesPerVolume;
            int framesPerVolume = samplesPerVolume / samplesPerFrame;
            int numCycles = readbackMicrons.Length / period;
            int riseSamples = (int)Math.Round(sawtoothShape * period);
            int pad = (int)Math.Round(PadFraction * period);
            if (numCycles - skipCycles < 1)
                return Failed($"Recorded {numCycles} volume(s), at least {skipCycles + 1} are needed.");
            if (riseSamples < 5)
                return Failed("The imaging flank is too short to analyze.");

            var onsets = new List<int>();
            var offsets = new List<int>();
            var velocities = new List<double>();
            int boundaryLimited = 0;
            for (int c = skipCycles; c < numCycles; c++)
            {
                int flankStart = c * period;
                int flankEnd = flankStart + riseSamples; // exclusive
                // Pad the search window but never cross into the neighboring imaging flanks or past the data
                int lo = Math.Max(Math.Max(0, flankStart - period + riseSamples), flankStart - pad);
                int hi = Math.Min(Math.Min(readbackMicrons.Length, flankStart + period), flankEnd + pad); // exclusive
                var window = FindLinearWindow(readbackMicrons, lo, hi, flankStart, flankEnd, velocityTolerance, smooth);
                if (window == null)
                    continue;
                var (start, end, vRef, limited) = window.Value;
                if (limited)
                    boundaryLimited++;
                onsets.Add(start - flankStart);
                offsets.Add(end - flankStart);
                velocities.Add(vRef);
            }
            if (onsets.Count == 0)
                return Failed("No cycle could be analyzed.");

            double velocity = Median(velocities.ToArray());
            // Latest onset and earliest offset is safe on every cycle. Frames only exist within the volume
            int windowStart = Math.Max(0, onsets.Max());
            int windowEnd = Math.Min(period - 1, offsets.Min());

            // Cycle averages for display and per-frame read-back positions
            int cyclesUsed = numCycles - skipCycles;
            var avgCommand = new double[period];
            var avgReadback = new double[period];
            for (int c = skipCycles; c < numCycles; c++)
            {
                for (int i = 0; i < period; i++)
                {
                    avgCommand[i] += commandMicrons[c * period + i] / cyclesUsed;
                    avgReadback[i] += readbackMicrons[c * period + i] / cyclesUsed;
                }
            }
            var planePositions = new double[framesPerVolume];
            for (int f = 0; f < framesPerVolume; f++)
            {
                double sum = 0;
                for (int i = f * samplesPerFrame; i < (f + 1) * samplesPerFrame; i++)
                    sum += avgReadback[i];
                planePositions[f] = sum / samplesPerFrame;
            }

            if (velocity <= 0)
                return Failed("The piezo read-back does not increase during the imaging flank. Check the read-back wiring and the piezo controller.",
                    cyclesUsed, avgCommand, avgReadback, planePositions);
            if (windowEnd <= windowStart)
                return Failed("The linear windows of the individual cycles do not overlap.", cyclesUsed, avgCommand, avgReadback, planePositions);

            // A frame is usable if its whole exposure lies inside the common window
            int firstPlane = (windowStart + samplesPerFrame - 1) / samplesPerFrame;
            int lastPlane = (windowEnd + 1) / samplesPerFrame - 1;
            if (lastPlane < firstPlane)
                return Failed("The linear window is shorter than one frame.", cyclesUsed, avgCommand, avgReadback, planePositions);

            string message = $"{lastPlane - firstPlane + 1} of {framesPerVolume} frames per volume are inside the linear window " +
                $"(analyzed {onsets.Count} volumes).";
            if (boundaryLimited > 0)
                message += $" On {boundaryLimited} volume(s) the window reached the edge of the search range, so it may be wider than reported.";
            return new PiezoLinearityResult
            {
                IsValid = true,
                Message = message,
                CyclesAnalyzed = onsets.Count,
                CyclesBoundaryLimited = boundaryLimited,
                WindowStartSample = windowStart,
                WindowEndSample = windowEnd,
                VelocityMicronsPerSample = velocity,
                FirstLinearPlane = firstPlane,
                LastLinearPlane = lastPlane,
                PlanePositionsMicrons = planePositions,
                CycleAverageCommand = avgCommand,
                CycleAverageReadback = avgReadback,
            };
        }

        /// <summary>
        /// Within the search range [lo, hi) of one cycle, finds the contiguous window around the center of the command's
        /// imaging flank [flankStart, flankEnd) over which the velocity stays within tolerance of the reference velocity.
        /// The reference velocity is estimated from the central part of the command's own flank only, so that the padding
        /// never biases it
        /// </summary>
        /// <returns>Start and end (both inclusive, absolute sample indices) of the window, the reference velocity and whether the
        /// window ran into the edge of the search range, or null if the range is too short</returns>
        private static (int start, int end, double vRef, bool boundaryLimited)? FindLinearWindow(double[] y, int lo, int hi, int flankStart, int flankEnd,
            double tolerance, int smoothSamples)
        {
            int n = hi - lo;
            if (n < 5 || flankEnd <= flankStart)
                return null;
            var velocity = LocalSlope(y, lo, hi, smoothSamples);

            int flankLength = flankEnd - flankStart;
            int c0 = flankStart + Math.Max(0, (int)(flankLength * (0.5 - CenterFraction / 2)));
            int c1 = flankStart + Math.Min(flankLength, (int)(flankLength * (0.5 + CenterFraction / 2)));
            if (c1 <= c0)
                c1 = c0 + 1;
            var central = new double[c1 - c0];
            Array.Copy(velocity, c0 - lo, central, 0, central.Length);
            double vRef = Median(central);
            double tol = Math.Abs(vRef) * tolerance;

            int center = (flankStart + flankEnd) / 2 - lo;
            int wLo = center, wHi = center;
            while (wLo > 0 && Math.Abs(velocity[wLo - 1] - vRef) <= tol)
                wLo--;
            while (wHi < n - 1 && Math.Abs(velocity[wHi + 1] - vRef) <= tol)
                wHi++;
            bool limited = wLo == 0 || wHi == n - 1;
            return (wLo + lo, wHi + lo, vRef, limited);
        }

        /// <summary>
        /// Velocity (per sample) of y[lo..hi) as the slope of a least squares line through a centered window of
        /// smoothSamples samples (shrunk at the edges). Fitting the position before differentiating avoids amplifying
        /// sample-to-sample noise. This is the first derivative of a Savitzky-Golay filter of order 1 (identical to order 2)
        /// </summary>
        internal static double[] LocalSlope(double[] y, int lo, int hi, int smoothSamples)
        {
            int n = hi - lo;
            int half = Math.Max(2, smoothSamples / 2);
            var slope = new double[n];
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - half);
                int b = Math.Min(n - 1, i + half);
                int m = b - a + 1;
                double xMean = (a + b) / 2.0;
                double yMean = 0;
                for (int j = a; j <= b; j++)
                    yMean += y[lo + j];
                yMean /= m;
                double sxy = 0, sxx = 0;
                for (int j = a; j <= b; j++)
                {
                    double dx = j - xMean;
                    sxy += dx * (y[lo + j] - yMean);
                    sxx += dx * dx;
                }
                slope[i] = sxx > 0 ? sxy / sxx : 0;
            }
            return slope;
        }

        private static double Median(double[] values)
        {
            var sorted = (double[])values.Clone();
            Array.Sort(sorted);
            int mid = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[mid] : 0.5 * (sorted[mid - 1] + sorted[mid]);
        }

        private static PiezoLinearityResult Failed(string message, int cycles = 0, double[]? command = null, double[]? readback = null, double[]? planes = null)
        {
            return new PiezoLinearityResult
            {
                IsValid = false,
                Message = message,
                CyclesAnalyzed = cycles,
                CycleAverageCommand = command ?? [],
                CycleAverageReadback = readback ?? [],
                PlanePositionsMicrons = planes ?? [],
            };
        }
    }
}
