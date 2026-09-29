using Hamamatsu.Dcam;
using Hamamatsu.Native;
using MatjesImager.Hardware;
using MatjesUtils;
using NationalInstruments.DAQmx;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MatjesImager.ViewModels
{
    public unsafe class TestViewModel : ViewModelBase
    {
        private EZImageSource? _camDisplay;

        public EZImageSource CamDisplay
        {
            get { return _camDisplay; }
        }

        private double _sheet1LeftVolts;

        public double Sheet1LeftVolts
        {
            get { return _sheet1LeftVolts; }
            set { _sheet1LeftVolts = value; RaisePropertyChanged(nameof(Sheet1LeftVolts)); }
        }

        private double _sheet1RightVolts;

        public double Sheet1RightVolts
        {
            get { return _sheet1RightVolts; }
            set { _sheet1RightVolts = value; RaisePropertyChanged(nameof(Sheet1RightVolts)); }
        }

        private double _sheet2LeftVolts;

        public double Sheet2LeftVolts
        {
            get { return _sheet2LeftVolts; }
            set { _sheet2LeftVolts = value; RaisePropertyChanged(nameof(Sheet2LeftVolts)); }
        }

        private double _sheet2RightVolts;

        public double Sheet2RightVolts
        {
            get { return _sheet2RightVolts; }
            set { _sheet2RightVolts = value; RaisePropertyChanged(nameof(Sheet2RightVolts)); }
        }

        private double _z1_fixed;

        public double Z1_Fixed
        {
            get { return _z1_fixed; }
            set { _z1_fixed = value; RaisePropertyChanged(nameof(Z1_Fixed));}
        }

        private double _z2_fixed;

        public double Z2_Fixed
        {
            get { return _z2_fixed; }
            set { _z2_fixed = value; RaisePropertyChanged(nameof(Z2_Fixed)); }
        }

        private double _piezo_fixed;

        public double Piezo_Fixed
        {
            get { return _piezo_fixed; }
            set { _piezo_fixed = value; RaisePropertyChanged(nameof(Piezo_Fixed)); RaisePropertyChanged(nameof(Piezo_Microns)); }
        }

        public string Piezo_Microns
        {
            get { return string.Format("{0:F2} uM", _piezo_fixed * 45); }
        }

        private int _frameIndex = 0;

        public int FrameIndex
        {
            get { return _frameIndex; }
            set { _frameIndex = value; RaisePropertyChanged(nameof(FrameIndex));  }
        }

        private double _frameRate = 0;

        public double FrameRate
        {
            get { return _frameRate; }
            set { _frameRate = value; RaisePropertyChanged(nameof(FrameRate)); }
        }

        private double _image_scale_min = 0.0;

        public double ImageScaleMin
        {
            get{return _image_scale_min; }
            set{_image_scale_min = value; RaisePropertyChanged(nameof(ImageScaleMin)); }
        }

        private double _image_scale_max = ushort.MaxValue;

        public double ImageScaleMax
        {
            get { return _image_scale_max; }
            set { _image_scale_max = value; RaisePropertyChanged(nameof(ImageScaleMax));}
        }

        private ScanControl _scanhead;

        public ScanControl Scanhead
        {
            get { return _scanhead; }
            private set { _scanhead = value; }
        }

        Image8? _camImage;

        Image16? _camImage16;

        private DcamCamera? _camera; // Using our custom P/Invoke wrapper
        private bool _isAcquiring = false;
        private CancellationTokenSource? _cancellationTokenSource;

        public TestViewModel() {
            Sheet1LeftVolts = -1;
            Sheet1RightVolts = 1;
            Sheet2LeftVolts = -0.5;
            Sheet2RightVolts = 0.5;
            Z1_Fixed = 0;
            Z2_Fixed = 0;
            Piezo_Fixed = 0;
            Scanhead = new ScanControl();
            if (IsInDesignMode)
                return;
            _camDisplay = new EZImageSource_LH();
            StartAcquisition();
            // Camera is now armed, start scanhead
            Scanhead.StartIdleScan(100);
        }

        /// <summary>
        /// Starts camera acquisition - note acquisition has to be started and armed before starting the Scanhead
        /// </summary>
        public void StartAcquisition()
        {
            try
            {
                // Initialize Camera
                _camera = new DcamCamera(0);
                _camera.SetPixelType(DcamNative.DCAM_PIXELTYPE.DCAM_PIXELTYPE_MONO16);
                _camera.SetReadoutSpeed(DcamNative.DCAM_READOUT_SPEED.DCAMPROP_READOUT_SPEED_FAST);
                _camera.SetROI(xOffset: 0, yOffset: 160, width: 2304, height: 2048);
                _camera.ConfigureHardwareTrigger();
                // Allocate Buffers and Arm Camera
                _camera.AllocateBuffer(10);
                _camera.StartCapture(DcamNative.DCAMCAP_START.SEQUENCE);
                // Acknowledge acquisition and generate cancelletation token
                _isAcquiring = true;
                _cancellationTokenSource = new CancellationTokenSource();
                // Launch image receiving thread - NOTE: The camera will wait on the counter due to triggering
                System.Threading.Tasks.Task.Run(() => AcquisitionLoop(_cancellationTokenSource.Token));
            }
            catch (Exception)
            {
                Cleanup();
                throw;
            }
        }

        private void AcquisitionLoop(CancellationToken token)
        {
            FrameIndex = 0;
            double all_deltas = 0;
            double delta;
            double current;
            double last = -1;
            while (_isAcquiring && !token.IsCancellationRequested)
            {
                // Using the custom wait method
                if (_camera.WaitForFrame(2000))
                {
                    // Using the custom transfer methods
                    _camera.GetTransferInfo(out int frameCount, out int newestFrameIndex);

                    DcamNative.DCAMBUF_FRAME frame = _camera.LockFrame(newestFrameIndex);
                    ProcessFrame(frame.buf, frame.width, frame.height, frame.rowbytes, token);
                    FrameIndex++;
                    current = frame.timestamp_sec + (double)frame.timestamp_microsec / 1000000.0;
                    if (last > 0)
                    {
                        delta = current - last;
                        all_deltas += delta;   
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
            if (_camImage == null || _camImage.Width != width || _camImage.Height != height)
            {
                _camImage = new Image8(new ipp.IppiSize(width, height));
                _camImage16 = new Image16(width, height);
            }
            ipp.ip.ippiCopy_16u_C1R((ushort*)unmanagedBuffer, rowBytes, _camImage16.Image, _camImage16.Stride, _camImage16.Size);
            double scale_factor = (double)byte.MaxValue / (ImageScaleMax - ImageScaleMin);
            double min_value = -ImageScaleMin * scale_factor;
            ipp.ip.ippiScaleC_16u8u_C1R(_camImage16.Image, _camImage16.Stride, scale_factor, min_value, _camImage.Image, _camImage.Stride, _camImage16.Size, ipp.IppHintAlgorithm.ippAlgHintFast);
            if (FrameIndex % 10 == 0)
            {
                try
                {
                    CamDisplay.Write(_camImage, token.WaitHandle);
                }
                catch (OperationCanceledException) { }
            }
        }

        public void StopAcquisition()
        {
            _isAcquiring = false;
            Scanhead.Stop();
            _cancellationTokenSource?.Cancel();

            if (_camera != null)
            {
                _camera.StopCapture();
                _camera.ReleaseBuffer();
            }
        }

        private void Cleanup()
        {
            Scanhead.Dispose();

            _camera?.Dispose();
            _camera = null;

            _camDisplay?.Dispose();

            _camImage?.Dispose();
        }

        override protected void Dispose(bool disposing)
        {
            StopAcquisition();
            Cleanup();
        }
    }
    }
