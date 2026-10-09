using MatjesImager.Properties;
using MatjesUtils;
using System;
using System.Collections.Generic;
using System.Text;

namespace MatjesImager.Hardware
{
    /// <summary>
    /// Describes the type of experiment to run during the volume scan
    /// </summary>
    internal enum VolumeScanType { ScanOnly=0 }

    /// <summary>
    /// Describes paramters for a volume scan
    /// </summary>
    internal struct VolumeScanParams
    {
        /// <summary>
        /// The camera frame rate during the scan
        /// </summary>
        public int CameraFrameRate { get; private set; }

        /// <summary>
        /// The number of frames that should make up one volume
        /// (Note: This isn't necessarily the number of frames that will occur during the linear phase of piezo movement)
        /// </summary>
        public int FramesPerVolume { get; private set; }

        /// <summary>
        /// The piezo scan starting position in microns
        /// </summary>
        public double StartMicrons {  get; private set; }

        /// <summary>
        /// The depth of the stack in microns
        /// </summary>
        public double DepthMicrons { get; private set; }

        /// <summary>
        /// The experimental paradigm to run during the scan
        /// </summary>
        public VolumeScanType Paradigm { get; private set; }

        /// <summary>
        /// The converter to convert between piezo and z volts for sheet 1
        /// </summary>
        public LinearZConverter ZConverterSheet1 { get; private set; }

        /// <summary>
        /// The converter to convert between piezo and z volts for sheet 2
        /// </summary>
        public LinearZConverter ZConverterSheet2 { get; private set; }

        public VolumeScanParams(int cameraFrameRate, int framesPerVolume, double startMicrons, double depthMicrons, VolumeScanType paradigm, LinearZConverter zConverterSheet1, LinearZConverter zConverterSheet2)
        {
            if (cameraFrameRate < 1)
                throw new ArgumentOutOfRangeException(nameof(cameraFrameRate), "Camera frame rate has to be larger than 0.");
            if (framesPerVolume < 1)
                throw new ArgumentOutOfRangeException(nameof(framesPerVolume), "Frames per volume hast to be larger than 0.");
            if (startMicrons < 0)
                throw new ArgumentOutOfRangeException(nameof(startMicrons), "Starting position has to be 0 or larger.");
            // TODO: Think about whether depthMicrons<0 should be enforced - we could allow scanning ventral to dorsal, however that would mean to invert the sawtooth shape parameter and the logic for determining linear phase
            if (depthMicrons < 0)
                throw new ArgumentOutOfRangeException(nameof(depthMicrons), "Stack depth has to be positive.");
            if (startMicrons + depthMicrons > Settings.Default.PiezoRangeMicrons)
                throw new ArgumentOutOfRangeException(nameof(startMicrons), "Stack end position is outside of piezo range.");
            ArgumentNullException.ThrowIfNull(zConverterSheet1);
            ArgumentNullException.ThrowIfNull(zConverterSheet2);
            CameraFrameRate = cameraFrameRate;
            FramesPerVolume = framesPerVolume;
            StartMicrons = startMicrons;
            DepthMicrons = depthMicrons;
            Paradigm = paradigm;
            ZConverterSheet1 = zConverterSheet1;
            ZConverterSheet2 = zConverterSheet2;
        }
    }

    /// <summary>
    /// Determines the width of both scan sheets which is fixed during volume scans
    /// </summary>
    internal struct SheetParams
    {
        /// <summary>
        /// The left side of sheet 1
        /// </summary>
        public double Sheet1LeftVolts { get; private set; }

        /// <summary>
        /// The right side of sheet 1
        /// </summary>
        public double Sheet1RightVolts { get; private set; }

        /// <summary>
        /// The left side of sheet 2
        /// </summary>
        public double Sheet2LeftVolts { get; private set; }

        /// <summary>
        /// The right side of sheet 2
        /// </summary>
        public double Sheet2RightVolts { get; private set; }

        public SheetParams(double sheet1LeftVolts, double sheet1RightVolts, double sheet2LeftVolts, double sheet2RightVolts)
        {
            if (sheet1LeftVolts > sheet1RightVolts)
                throw new ArgumentException("Sheet 1 right voltage has to be at least sheet 1 left voltage");
            if (sheet2LeftVolts > sheet2RightVolts)
                throw new ArgumentException("Sheet 2 right voltage has to be at least sheet 1 left voltage");
            Sheet1LeftVolts = sheet1LeftVolts;
            Sheet1RightVolts = sheet1RightVolts;
            Sheet2LeftVolts = sheet2LeftVolts;
            Sheet2RightVolts = sheet2RightVolts;
        }
    }
}
