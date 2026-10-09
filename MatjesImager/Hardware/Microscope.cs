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

        public Microscope()
        {
            ScanHead = new ScanControl();
            // On first creation of the microscope, zero all analog outputs and set up our piezo controller
            ScanHead.SetAllAOZero();
            PiezoConfig.ConfigurePPC001(Properties.Settings.Default.PiezoSerialNumber);// Set Closed loop, external BNC control and corrected position report on Piezo
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
