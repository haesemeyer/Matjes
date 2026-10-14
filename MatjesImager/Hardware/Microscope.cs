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

        public void StartIdleScan(int frameRateHz, IAnalogProcessor<Tuple<long, double>>? hardware_ai_processor=null)
        {
            if (IsRunning)
                throw new InvalidOperationException("Cannot start new scan while microscope is running");
            Camera = new CameraStream();
            // Camera has to be armed before the scanhead starts the trigger counter
            Camera.Start();
            if(hardware_ai_processor != null)
            {
                // TODO: It is not ideal that we compute sampleRate in multiple places. Ideally this should be queriable from ScanControl, which however currently only computes this value after starting!
                int sampleRate = Settings.Default.SamplesPerFrame * frameRateHz;
                _analogProcessor = hardware_ai_processor;
                HardwareRead = new AIStream(4 * sampleRate, 10, true);
                // Start our analog in reads before starting the scan head as they wait on the same trigger as the camera
                _analogProcessor.Start(HardwareRead.HardwareStream);
                HardwareRead.Start(sampleRate);
            }
            ScanHead.StartIdleScan(frameRateHz);
            Mode = MicroscopeMode.Preview;
        }

        public void StartVolumeScan(VolumeScanParams scanParams)
        {
            if (IsRunning)
                throw new InvalidOperationException("Cannot start new scan while microscope is running");
            Camera = new CameraStream();
            // Camera has to be armed before the scanhead starts the trigger counter
            Camera.Start();
            ScanHead.StartZScan(scanParams);
            Mode = MicroscopeMode.VolumeScan;
        }

        public void Stop()
        {
            if (!IsRunning)
                return;
            _analogProcessor?.Stop();
            Camera?.Stop();
            HardwareRead?.Stop();
            ScanHead.Stop();
            ScanHead.SetAllAOZero();
            Camera?.Dispose();
            Camera = null;
            Mode = MicroscopeMode.Off;
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
