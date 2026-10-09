using MatjesUtils;
using System;
using System.Collections.Generic;
using System.Text;

namespace MatjesImager.Hardware
{
    public unsafe class Microscope : PropertyChangeNotification, IDisposable
    {
        private bool _isRunning = false;

        public ScanControl ScanHead { get; private set; }

        public CameraStream? Camera { get; private set;  }

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

        public void StartIdleScan(int frameRateHz)
        {
            if (_isRunning)
                throw new InvalidOperationException("Cannot start new scan while microscope is running");
            Camera = new CameraStream();
            // Camera has to be armed before the scanhead starts the trigger counter
            Camera.Start();
            ScanHead.StartIdleScan(frameRateHz);
            _isRunning = true;
        }

        public void StartVolumeScan(VolumeScanParams scanParams)
        {
            if (_isRunning)
                throw new InvalidOperationException("Cannot start new scan while microscope is running");
            Camera = new CameraStream();
            // Camera has to be armed before the scanhead starts the trigger counter
            Camera.Start();
            ScanHead.StartZScan(scanParams);
            _isRunning = true;
        }

        public void Stop()
        {
            if (!_isRunning)
                return;
            Camera?.Stop();
            ScanHead.Stop();
            ScanHead.SetAllAOZero();
            Camera?.Dispose();
            Camera = null;
            _isRunning = false;
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
