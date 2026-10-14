using System;
using System.Collections.Generic;
using System.Text;

namespace MatjesImager.Hardware
{
    /// <summary>
    /// A tested and accepted volume scan: the scan parameters and the planes (frames within each volume) to keep.
    /// Only frames on the linear part of the piezo sawtooth are kept, so there are fewer planes than frames per volume
    /// </summary>
    public class VolumeScanConfig
    {
        /// <param name="scanParams">The parameters the scan was tested with</param>
        /// <param name="firstPlane">The first frame within each volume to keep</param>
        /// <param name="lastPlane">The last frame within each volume to keep (inclusive)</param>
        /// <param name="planePositionsMicrons">The measured piezo position of every frame of the volume</param>
        public VolumeScanConfig(VolumeScanParams scanParams, int firstPlane, int lastPlane, double[] planePositionsMicrons)
        {
            ArgumentNullException.ThrowIfNull(planePositionsMicrons);
            if (planePositionsMicrons.Length != scanParams.FramesPerVolume)
                throw new ArgumentException("There has to be one plane position per frame of the volume.", nameof(planePositionsMicrons));
            if (firstPlane < 0 || lastPlane < firstPlane || lastPlane >= scanParams.FramesPerVolume)
                throw new ArgumentOutOfRangeException(nameof(firstPlane), "The kept planes have to be a non-empty range within the volume.");
            ScanParams = scanParams;
            FirstPlane = firstPlane;
            LastPlane = lastPlane;
            PlanePositionsMicrons = (double[])planePositionsMicrons.Clone();
        }

        /// <summary>
        /// The parameters of the volume scan
        /// </summary>
        public VolumeScanParams ScanParams { get; }

        /// <summary>
        /// The first frame within each volume to keep
        /// </summary>
        public int FirstPlane { get; }

        /// <summary>
        /// The last frame within each volume to keep (inclusive)
        /// </summary>
        public int LastPlane { get; }

        /// <summary>
        /// The number of planes kept per volume
        /// </summary>
        public int NumPlanes => LastPlane - FirstPlane + 1;

        /// <summary>
        /// The measured piezo position (in microns) of every frame of the volume, averaged over the frame's exposure
        /// </summary>
        public IReadOnlyList<double> PlanePositionsMicrons { get; }

        /// <summary>
        /// Indicates whether a frame (by its plane index within the volume) is kept
        /// </summary>
        public bool IsKept(int plane) => plane >= FirstPlane && plane <= LastPlane;
    }
}
