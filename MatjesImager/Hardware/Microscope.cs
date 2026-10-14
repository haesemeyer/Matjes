using MatjesImager.Properties;
using MatjesUtils;
using System;
using System.Collections.Generic;
using System.Text;

namespace MatjesImager.Hardware
{
    /// <summary>
    /// What the microscope is currently doing
    /// </summary>
    public enum MicroscopeMode { Off = 0, Preview = 1, VolumeScan = 2 }

    public unsafe class Microscope : PropertyChangeNotification, IDisposable
    {
        private MicroscopeMode _mode = MicroscopeMode.Off;

        private CameraStream? _camera;

        private AIStream? _hardwareRead;

        private IAnalogProcessor<Tuple<long, double>>? _analogProcessor;

        public ScanControl ScanHead { get; private set; }

        /// <summary>
        /// The camera stream of the current scan. A new stream is created on each scan start and it is null while stopped
        /// </summary>
        public CameraStream? Camera
        {
            get { return _camera; }
            private set { _camera = value; RaisePropertyChanged(nameof(Camera)); }
        }

        /// <summary>
        /// If requested the analog in reader for hardware readback
        /// </summary>
        public AIStream? HardwareRead
        {
            get { return _hardwareRead; }
            private set { _hardwareRead = value; RaisePropertyChanged(nameof(HardwareRead));  }
        }

        /// <summary>
        /// The current mode: Preview is the idle scan with fixed, user-set positions
        /// </summary>
        public MicroscopeMode Mode
        {
            get { return _mode; }
            private set { _mode = value; RaisePropertyChanged(nameof(Mode)); RaisePropertyChanged(nameof(IsRunning)); }
        }

        /// <summary>
        /// Indicates whether a scan is running
        /// </summary>
        public bool IsRunning => Mode != MicroscopeMode.Off;

        public Microscope() : this(designInstance: false) { }

        private Microscope(bool designInstance)
        {
            ScanHead = new ScanControl();
            // ScanControl does not access hardware on construction so the designer can bind to it.
            // Camera stays null at design time since its display source needs the UI dispatcher set up by App
            if (designInstance)
                return;
            // On first creation of the microscope, zero all analog outputs and set up our piezo controller
            ScanHead.SetAllAOZero();
            PiezoConfig.ConfigurePPC001(Properties.Settings.Default.PiezoSerialNumber);// Set Closed loop, external BNC control and corrected position report on Piezo
        }

        /// <summary>
        /// Creates a stand-in for the XAML designer that never touches the DAQ boards, piezo or camera.
        /// Do not start scans on this instance
        /// </summary>
        internal static Microscope CreateDesignInstance()
        {
            return new Microscope(designInstance: true);
        }

        /// <summary>
        /// The number of analog-in samples averaged into one read-back sample. It divides the samples per frame,
        /// so that read-back sample n covers z samples n * HardwareReadSamplesToAverage onwards and every frame
        /// spans a whole number of read-back samples
        /// </summary>
        public static int HardwareReadSamplesToAverage => Settings.Default.SamplesPerFrame % 10 == 0 ? 10 : 1;

        /// <summary>
        /// Starts the idle scan with fixed, user-set positions
        /// </summary>
        /// <param name="frameRateHz">The camera frame rate</param>
        /// <param name="hardware_ai_processor">If not null, consumer of the piezo read-back during the scan</param>
        public void StartIdleScan(int frameRateHz, IAnalogProcessor<Tuple<long, double>>? hardware_ai_processor=null)
        {
            StartScan(frameRateHz, hardware_ai_processor, null, () => ScanHead.StartIdleScan(frameRateHz), MicroscopeMode.Preview);
        }

        /// <summary>
        /// Starts a volume scan. The camera tags each frame with its plane within the volume
        /// </summary>
        /// <param name="scanParams">The scan parameters</param>
        /// <param name="hardware_ai_processor">If not null, consumer of the piezo read-back during the scan</param>
        public void StartVolumeScan(VolumeScanParams scanParams, IAnalogProcessor<Tuple<long, double>>? hardware_ai_processor = null)
        {
            StartScan(scanParams.CameraFrameRate, hardware_ai_processor,
                camera => camera.ConfigureVolumeScan(scanParams.FramesPerVolume, scanParams.CameraFrameRate),
                () => ScanHead.StartZScan(scanParams), MicroscopeMode.VolumeScan);
        }

        /// <summary>
        /// Arms the camera, starts the optional analog-in reads and then the scan head, whose counter triggers everything.
        /// On failure all hardware that was started is stopped again
        /// </summary>
        private void StartScan(int frameRateHz, IAnalogProcessor<Tuple<long, double>>? hardware_ai_processor, Action<CameraStream>? configureCamera,
            Action startScanHead, MicroscopeMode mode)
        {
            if (IsRunning)
                throw new InvalidOperationException("Cannot start new scan while microscope is running");
            try
            {
                Camera = new CameraStream();
                configureCamera?.Invoke(Camera);
                // Camera has to be armed before the scanhead starts the trigger counter
                Camera.Start();
                if (hardware_ai_processor != null)
                {
                    // TODO: It is not ideal that we compute sampleRate in multiple places. Ideally this should be queriable from ScanControl, which however currently only computes this value after starting!
                    int sampleRate = Settings.Default.SamplesPerFrame * frameRateHz;
                    _analogProcessor = hardware_ai_processor;
                    HardwareRead = new AIStream(4 * sampleRate, HardwareReadSamplesToAverage, true);
                    // Start our analog in reads before starting the scan head as they wait on the same trigger as the camera
                    _analogProcessor.Start(HardwareRead.HardwareStream);
                    HardwareRead.Start(sampleRate);
                }
                startScanHead();
            }
            catch (Exception)
            {
                // Report the original failure rather than one from cleaning up after it
                try { StopHardware(); }
                catch (Exception) { }
                throw;
            }
            Mode = mode;
        }

        public void Stop()
        {
            if (!IsRunning)
                return;
            StopHardware();
            Mode = MicroscopeMode.Off;
        }

        /// <summary>
        /// Stops and releases everything a scan started. Safe to call on partially started scans
        /// </summary>
        private void StopHardware()
        {
            _analogProcessor?.Stop();
            _analogProcessor = null;
            Camera?.Stop();
            HardwareRead?.Stop();
            ScanHead.Stop();
            ScanHead.SetAllAOZero();
            HardwareRead?.Dispose();
            HardwareRead = null;
            Camera?.Dispose();
            Camera = null;
        }

        #region IDisposable
        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Stop();
                    ScanHead.Dispose();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
