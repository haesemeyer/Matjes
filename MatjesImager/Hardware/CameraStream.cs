using Hamamatsu.Dcam;
using Hamamatsu.Native;
using MatjesUtils;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace MatjesImager.Hardware
{
    /// <summary>
    /// Encapsulates hardware triggered acquisition from the Fusion camera, conversion of the 16-bit
    /// camera frames to 8-bit display images and writing them to a display source.
    /// Acquisition logic follows TestViewModel but is kept in its own class so that multiple views can share it.
    /// NOTE: The camera waits on the counter of ScanControl for triggering, so Start has to be called before
    /// starting a scan on the ScanControl object
    /// </summary>
    public unsafe class CameraStream : PropertyChangeNotification, IDisposable
    {
        #region Members

        private DcamCamera? _camera;

        private EZImageSource? _display;

        private Image8? _camImage;

        private Image16? _camImage16;

        /// <summary>
        /// Guards _camImage so that the latest frame can be copied from the UI thread while frames arrive
        /// NOTE: Never hold this lock while writing to the display since that blocks on the UI thread
        /// </summary>
        private readonly object _frameLock = new object();

        private bool _isAcquiring = false;

        private CancellationTokenSource? _cancellationTokenSource;

        private System.Threading.Tasks.Task? _acquisitionTask;

        private bool disposedValue;

        private int _frameIndex = 0;

        private double _frameRate = 0;

        private double _image_scale_min = 0.0;

        private double _image_scale_max = ushort.MaxValue;

        #endregion

        /// <summary>
        /// Creates a new camera stream
        /// </summary>
        /// <param name="displayEvery">Every displayEvery-th frame will be written to the display</param>
        public CameraStream(int displayEvery = 10)
        {
            DisplayEvery = displayEvery;
            _display = new EZImageSource_LH();
        }

        #region Properties

        /// <summary>
        /// The display source for the live camera stream
        /// </summary>
        public EZImageSource? Display => _display;

        /// <summary>
        /// Every DisplayEvery-th frame will be written to the display
        /// </summary>
        public int DisplayEvery { get; set; }

        /// <summary>
        /// The number of frames received since acquisition start
        /// </summary>
        public int FrameIndex
        {
            get { return _frameIndex; }
            private set { _frameIndex = value; RaisePropertyChanged(nameof(FrameIndex)); }
        }

        /// <summary>
        /// The average frame rate since acquisition start based on camera timestamps
        /// </summary>
        public double FrameRate
        {
            get { return _frameRate; }
            private set { _frameRate = value; RaisePropertyChanged(nameof(FrameRate)); }
        }

        /// <summary>
        /// 16-bit value that will be mapped to 0 in the 8-bit image
        /// </summary>
        public double ImageScaleMin
        {
            get { return _image_scale_min; }
            set { _image_scale_min = value; RaisePropertyChanged(nameof(ImageScaleMin)); }
        }

        /// <summary>
        /// 16-bit value that will be mapped to 255 in the 8-bit image
        /// </summary>
        public double ImageScaleMax
        {
            get { return _image_scale_max; }
            set { _image_scale_max = value; RaisePropertyChanged(nameof(ImageScaleMax)); }
        }

        /// <summary>
        /// Indicates whether acquisition is running
        /// </summary>
        public bool IsAcquiring => _isAcquiring;

        #endregion

        #region Methods

        /// <summary>
        /// Opens and arms the camera for hardware triggered acquisition and starts the receiving thread
        /// </summary>
        public void Start()
        {
            if (_isAcquiring)
                throw new InvalidOperationException("Invoked Start while camera acquisition is running");
            try
            {
                _camera = new DcamCamera(0);
                _camera.SetPixelType(DcamNative.DCAM_PIXELTYPE.DCAM_PIXELTYPE_MONO16);
                _camera.SetReadoutSpeed(DcamNative.DCAM_READOUT_SPEED.DCAMPROP_READOUT_SPEED_FAST);
                _camera.SetROI(xOffset: 0, yOffset: 160, width: 2304, height: 2048);
                _camera.ConfigureHardwareTrigger();
                _camera.AllocateBuffer(10);
                _camera.StartCapture(DcamNative.DCAMCAP_START.SEQUENCE);
                _isAcquiring = true;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();
                var token = _cancellationTokenSource.Token;
                _acquisitionTask = System.Threading.Tasks.Task.Run(() => AcquisitionLoop(token));
            }
            catch (Exception)
            {
                _isAcquiring = false;
                _camera?.Dispose();
                _camera = null;
                throw;
            }
        }

        /// <summary>
        /// Stops acquisition, waits for the receiving thread to finish and closes the camera
        /// </summary>
        public void Stop()
        {
            if (!_isAcquiring)
                return;
            _isAcquiring = false;
            _cancellationTokenSource?.Cancel();
            // Wait for the receiving thread to leave the camera alone before closing it. The thread
            // waits at most 2 s for a frame, so this returns at the latest after the frame timeout
            try { _acquisitionTask?.Wait(5000); }
            catch (AggregateException) { }
            _acquisitionTask = null;
            if (_camera != null)
            {
                _camera.StopCapture();
                _camera.ReleaseBuffer();
                _camera.Dispose();
                _camera = null;
            }
        }

        /// <summary>
        /// Returns a copy of the most recent 8-bit frame or null if no frame has been received yet.
        /// The caller owns (and has to dispose) the returned image
        /// </summary>
        public Image8? CopyLatestFrame()
        {
            lock (_frameLock)
            {
                if (_camImage == null || FrameIndex == 0)
                    return null;
                var copy = new Image8(_camImage.Size);
                ipp.ip.ippiCopy_8u_C1R(_camImage.Image, _camImage.Stride, copy.Image, copy.Stride, _camImage.Size);
                return copy;
            }
        }

        private void AcquisitionLoop(CancellationToken token)
        {
            FrameIndex = 0;
            FrameRate = 0;
            double all_deltas = 0;
            double current;
            double last = -1;
            while (_isAcquiring && !token.IsCancellationRequested)
            {
                if (_camera!.WaitForFrame(2000))
                {
                    _camera.GetTransferInfo(out int frameCount, out int newestFrameIndex);
                    DcamNative.DCAMBUF_FRAME frame = _camera.LockFrame(newestFrameIndex);
                    ProcessFrame(frame.buf, frame.width, frame.height, frame.rowbytes, token);
                    current = frame.timestamp_sec + (double)frame.timestamp_microsec / 1000000.0;
                    if (last > 0)
                    {
                        all_deltas += current - last;
                        FrameRate = (double)FrameIndex / all_deltas;
                    }
                    last = current;
                }
                else
                {
                    Console.WriteLine("Timeout waiting for frame.");
                }
            }
        }

        private void ProcessFrame(IntPtr unmanagedBuffer, int width, int height, int rowBytes, CancellationToken token)
        {
            lock (_frameLock)
            {
                if (_camImage == null || _camImage.Width != width || _camImage.Height != height)
                {
                    _camImage?.Dispose();
                    _camImage16?.Dispose();
                    _camImage = new Image8(new ipp.IppiSize(width, height));
                    _camImage16 = new Image16(width, height);
                }
                ipp.ip.ippiCopy_16u_C1R((ushort*)unmanagedBuffer, rowBytes, _camImage16!.Image, _camImage16.Stride, _camImage16.Size);
                double scale_factor = (double)byte.MaxValue / (ImageScaleMax - ImageScaleMin);
                double min_value = -ImageScaleMin * scale_factor;
                ipp.ip.ippiScaleC_16u8u_C1R(_camImage16.Image, _camImage16.Stride, scale_factor, min_value, _camImage.Image, _camImage.Stride, _camImage16.Size, ipp.IppHintAlgorithm.ippAlgHintFast);
            }
            // Writing to the display only reads _camImage on this thread, which is also the only writer
            if (FrameIndex % DisplayEvery == 0 && _display != null)
            {
                try
                {
                    _display.Write(_camImage!, token.WaitHandle);
                }
                catch (OperationCanceledException) { }
            }
            FrameIndex++;
        }

        #endregion

        #region IDisposable

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Stop();
                    _cancellationTokenSource?.Dispose();
                    _display?.Dispose();
                    lock (_frameLock)
                    {
                        _camImage?.Dispose();
                        _camImage16?.Dispose();
                    }
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
