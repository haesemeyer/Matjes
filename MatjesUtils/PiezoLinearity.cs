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
        /// Root mean square deviation, in microns, of the individual volumes' plane positions from their cycle average,
        /// over the planes inside the linear window. Captures volume-to-volume jitter such as small oscillations of the
        /// piezo, which the window itself is not based on
        /// </summary>
        public double PlaneJitterMicrons { get; init; }

        /// <summary>
        /// Start of the linear window, in samples from volume start (always a frame boundary)
        /// </summary>
        public int WindowStartSample { get; init; }

        /// <summary>
        /// End (inclusive) of the linear window, in samples from volume start (always the last sample of a frame)
        /// </summary>
        public int WindowEndSample { get; init; }

        /// <summary>
        /// The steady-state piezo velocity on the imaging flank in microns per sample
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
    /// Finds the frames of a piezo sawtooth volume whose read-back positions lie on an evenly spaced grid, from the position
    /// read-back recorded during a volume scan. The imaging flank used by ScanControl is the rising flank of the sawtooth.
    /// The piezo low-pass filters its command, so its read-back is curved after each direction reversal and only straight
    /// in between. Only frames exposed during the straight part have a uniform z-step.
    /// Linearity is judged on plane positions, not on velocity: each frame's position is the read-back averaged over its
    /// exposure and over all analyzed volumes, a line is fitted to the frames in the center of the flank, and the window
    /// grows outward from the center while frames stay within a tolerance (a fraction of the plane spacing) of that line.
    /// A velocity criterion fails when the piezo carries a load such as an objective, which makes it oscillate slightly
    /// around the straight path: the local velocity then swings by as much as its mean while the positions, which are what
    /// imaging depends on, deviate by a tiny fraction of a plane. Averaging over volumes removes such oscillations when they
    /// are not locked to the sawtooth; how much they move individual volumes is reported separately as PlaneJitterMicrons.
    /// All indices are in read-back samples. Sample n of the read-back has to be aligned with sample n of the command,
    /// which holds when both are triggered by the same counter edge and clocked at the same rate.
    /// </summary>
    public static class PiezoLinearity
    {
        /// <summary>
        /// Maximum deviation of a plane from the fitted evenly spaced grid, as a fraction of the plane spacing (0.1 = 10%)
        /// </summary>
        public const double DefaultPositionTolerance = 0.1;

        /// <summary>
        /// Central fraction of the command's imaging flank used to fit the reference line
        /// </summary>
        public const double CenterFraction = 0.5;

        /// <summary>
        /// Analyzes the piezo read-back of a volume scan
        /// </summary>
        /// <param name="commandMicrons">Piezo command per sample in microns</param>
        /// <param name="readbackMicrons">Piezo read-back per sample in microns, aligned with the command</param>
        /// <param name="samplesPerVolume">The number of samples in one volume (sawtooth period)</param>
        /// <param name="samplesPerFrame">The number of samples in one camera frame. Has to divide samplesPerVolume</param>
        /// <param name="sawtoothShape">Fraction of the period spent on the rising (imaging) flank</param>
        /// <param name="skipCycles">The number of initial cycles to ignore while the piezo settles into periodic motion</param>
        /// <param name="positionTolerance">Maximum deviation of a plane from the fitted grid as a fraction of the plane spacing</param>
        public static PiezoLinearityResult Analyze(double[] commandMicrons, double[] readbackMicrons, int samplesPerVolume, int samplesPerFrame,
            double sawtoothShape, int skipCycles = 1, double positionTolerance = DefaultPositionTolerance)
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
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(positionTolerance);

            int period = samplesPerVolume;
            int framesPerVolume = samplesPerVolume / samplesPerFrame;
            int numCycles = readbackMicrons.Length / period;
            // Frames whose whole exposure lies on the command's imaging flank
            int flankFrames = (int)Math.Round(sawtoothShape * period) / samplesPerFrame;
            if (numCycles - skipCycles < 1)
                return Failed($"Recorded {numCycles} volume(s), at least {skipCycles + 1} are needed.");
            if (flankFrames < 3)
                return Failed("The imaging flank is shorter than three frames, too short to analyze.");

            // Cycle averages for display
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
            // Plane positions: read-back averaged over each frame's exposure, per cycle and across cycles
            var cyclePlanes = new double[cyclesUsed, framesPerVolume];
            var planePositions = new double[framesPerVolume];
            for (int c = 0; c < cyclesUsed; c++)
            {
                int offset = (c + skipCycles) * period;
                for (int f = 0; f < framesPerVolume; f++)
                {
                    double sum = 0;
                    for (int i = f * samplesPerFrame; i < (f + 1) * samplesPerFrame; i++)
                        sum += readbackMicrons[offset + i];
                    cyclePlanes[c, f] = sum / samplesPerFrame;
                    planePositions[f] += cyclePlanes[c, f] / cyclesUsed;
                }
            }

            // Reference grid: line through the frames in the center of the flank, which are well away from either reversal
            int c0 = (int)(flankFrames * (0.5 - CenterFraction / 2));
            int c1 = Math.Min(flankFrames, Math.Max(c0 + 2, (int)Math.Ceiling(flankFrames * (0.5 + CenterFraction / 2)))); // exclusive
            var (intercept, spacing) = FitLine(planePositions, c0, c1);
            if (spacing <= 0)
                return Failed("The piezo read-back does not increase during the imaging flank. Check the read-back wiring and the piezo controller.",
                    cyclesUsed, avgCommand, avgReadback, planePositions);

            // Grow the window from the center while planes stay on the grid
            double tol = positionTolerance * spacing;
            bool OnGrid(int f) => Math.Abs(planePositions[f] - (intercept + spacing * f)) <= tol;
            int center = (c0 + c1 - 1) / 2;
            if (!OnGrid(center))
                return Failed("The plane positions in the center of the imaging flank do not lie on a line.", cyclesUsed, avgCommand, avgReadback, planePositions);
            int firstPlane = center, lastPlane = center;
            while (firstPlane > 0 && OnGrid(firstPlane - 1))
                firstPlane--;
            while (lastPlane < framesPerVolume - 1 && OnGrid(lastPlane + 1))
                lastPlane++;

            // Volume-to-volume jitter of the planes inside the window
            double sumSq = 0;
            for (int c = 0; c < cyclesUsed; c++)
                for (int f = firstPlane; f <= lastPlane; f++)
                    sumSq += Math.Pow(cyclePlanes[c, f] - planePositions[f], 2);
            double jitter = Math.Sqrt(sumSq / (cyclesUsed * (lastPlane - firstPlane + 1)));

            string message = $"{lastPlane - firstPlane + 1} of {framesPerVolume} frames per volume are inside the linear window " +
                $"(analyzed {cyclesUsed} volumes). Between volumes, planes vary by {jitter:F2} um RMS ({jitter / spacing:P0} of the plane spacing).";
            return new PiezoLinearityResult
            {
                IsValid = true,
                Message = message,
                CyclesAnalyzed = cyclesUsed,
                PlaneJitterMicrons = jitter,
                WindowStartSample = firstPlane * samplesPerFrame,
                WindowEndSample = (lastPlane + 1) * samplesPerFrame - 1,
                VelocityMicronsPerSample = spacing / samplesPerFrame,
                FirstLinearPlane = firstPlane,
                LastLinearPlane = lastPlane,
                PlanePositionsMicrons = planePositions,
                CycleAverageCommand = avgCommand,
                CycleAverageReadback = avgReadback,
            };
        }

        /// <summary>
        /// Least squares line through values[lo..hi) against their index
        /// </summary>
        /// <returns>Intercept (value at index 0) and slope per index</returns>
        private static (double intercept, double slope) FitLine(double[] values, int lo, int hi)
        {
            int n = hi - lo;
            double xMean = (lo + hi - 1) / 2.0;
            double yMean = 0;
            for (int i = lo; i < hi; i++)
                yMean += values[i];
            yMean /= n;
            double sxy = 0, sxx = 0;
            for (int i = lo; i < hi; i++)
            {
                double dx = i - xMean;
                sxy += dx * (values[i] - yMean);
                sxx += dx * dx;
            }
            double slope = sxy / sxx;
            return (yMean - slope * xMean, slope);
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
