//Google GEMINI
using System;
using System.Runtime.InteropServices;
using Hamamatsu.Native;

namespace Hamamatsu.Dcam
{
    public class DcamCamera : IDisposable
    {
        private IntPtr _hDcam = IntPtr.Zero;
        private IntPtr _hWait = IntPtr.Zero;
        private bool _isInitialized = false;

        public DcamCamera(int cameraIndex = 0)
        {
            InitializeApi();
            OpenDevice(cameraIndex);
            OpenWaitHandle();
        }

        private void InitializeApi()
        {
            var initParam = new DcamNative.DCAMAPI_INIT
            {
                size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(DcamNative.DCAMAPI_INIT))
            };

            var err = DcamNative.dcamapi_init(ref initParam);
            if (err != DcamNative.DCAMERR.SUCCESS)
                throw new Exception($"DCAM API Init failed: {err}");

            if (initParam.iDeviceCount == 0)
                throw new Exception("No DCAM cameras found.");

            _isInitialized = true;
        }

        private void OpenDevice(int index)
        {
            var openParam = new DcamNative.DCAMDEV_OPEN
            {
                size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(DcamNative.DCAMDEV_OPEN)),
                index = index
            };

            var err = DcamNative.dcamdev_open(ref openParam);
            if (err != DcamNative.DCAMERR.SUCCESS)
                throw new Exception($"Failed to open camera: {err}");

            _hDcam = openParam.hdcam;
        }

        private void OpenWaitHandle()
        {
            var waitParam = new DcamNative.DCAMWAIT_OPEN
            {
                size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(DcamNative.DCAMWAIT_OPEN)),
                hdcam = _hDcam
            };

            var err = DcamNative.dcamwait_open(ref waitParam);
            if (err != DcamNative.DCAMERR.SUCCESS)
                throw new Exception($"Failed to open wait handle: {err}");

            _hWait = waitParam.hwait;
        }

        // --- Property Accessors ---

        public void SetProperty(int propId, double value)
        {
            var err = DcamNative.dcamprop_setvalue(_hDcam, propId, value);
            if (err != DcamNative.DCAMERR.SUCCESS)
                throw new Exception($"Failed to set property {propId:X} to {value}: {err}");
        }

        public double GetProperty(int propId)
        {
            var err = DcamNative.dcamprop_getvalue(_hDcam, propId, out double value);
            if (err != DcamNative.DCAMERR.SUCCESS)
                throw new Exception($"Failed to get property {propId:X}: {err}");
            return value;
        }

        // Setup for Hardware Sync (External Edge Trigger)
        public void ConfigureHardwareTrigger(double exposureTimeSec)
        {
            SetProperty(DcamNative.DCAM_IDPROP_EXPOSURETIME, exposureTimeSec);

            // 1 = Internal, 2 = External. Set to External for Hardware Sync.
            SetProperty(DcamNative.DCAM_IDPROP_TRIGGERSOURCE, (int)DcamNative.DCAM_TRIGGERSOURCE.DCAMPROP_TRIGGERSOURCE__EXTERNAL);

            // 1 = Edge, 2 = Level, 3 = Synchronous Readout
            SetProperty(DcamNative.DCAM_IDPROP_TRIGGERACTIVE, (int)DcamNative.DCAM_TRIGGERACTIVE.DCAMPROP_TRIGGERACTIVE__EDGE);
        }

        public void ConfigureInternalTrigger(double exposureTimeSec)
        {
            SetProperty(DcamNative.DCAM_IDPROP_EXPOSURETIME, exposureTimeSec);

            // 1 = Internal, 2 = External. Set to External for Hardware Sync.
            SetProperty(DcamNative.DCAM_IDPROP_TRIGGERSOURCE, (int)DcamNative.DCAM_TRIGGERSOURCE.DCAMPROP_TRIGGERSOURCE__INTERNAL);

            // 1 = Edge, 2 = Level, 3 = Synchronous Readout
            SetProperty(DcamNative.DCAM_IDPROP_TRIGGERACTIVE, (int)DcamNative.DCAM_TRIGGERACTIVE.DCAMPROP_TRIGGERACTIVE__EDGE);
        }

        /// <summary>
        /// Sets the camera binning factor. 
        /// Typical values are 1 (1x1), 2 (2x2), or 4 (4x4) depending on the camera model.
        /// </summary>
        public void SetBinning(int binningFactor)
        {
            SetProperty(DcamNative.DCAM_IDPROP_BINNING, binningFactor);
        }

        /// <summary>
        /// Configures and enables a Region of Interest (Subarray).
        /// Note: The Orca Fusion requires sizes and positions to be multiples of 4. 
        /// The DCAM API will automatically round to the nearest valid value if needed.
        /// </summary>
        public void SetROI(int xOffset, int yOffset, int width, int height)
        {
            // 1. Enable Subarray mode (2 = ON)
            SetProperty(DcamNative.DCAM_IDPROP_SUBARRAYMODE, 2.0);

            // 2. Set dimensions first (preventing out-of-bounds errors with current positions)
            SetProperty(DcamNative.DCAM_IDPROP_SUBARRAYHSIZE, width);
            SetProperty(DcamNative.DCAM_IDPROP_SUBARRAYVSIZE, height);

            // 3. Set top-left offsets
            SetProperty(DcamNative.DCAM_IDPROP_SUBARRAYHPOS, xOffset);
            SetProperty(DcamNative.DCAM_IDPROP_SUBARRAYVPOS, yOffset);
        }

        /// <summary>
        /// Disables the Region of Interest, resetting the camera to full frame readout.
        /// </summary>
        public void DisableROI()
        {
            // 1 = OFF
            SetProperty(DcamNative.DCAM_IDPROP_SUBARRAYMODE, 1.0);
        }

        /// <summary>
        /// Sets the bit depth of the image sensor. 
        /// For Orca Fusion, common values are 16 or 12.
        /// </summary>
        public void SetBitDepth(int bitDepth)
        {
            SetProperty(DcamNative.DCAM_IDPROP_BITSPERCHANNEL, bitDepth);
        }

        public void SetPixelType(DcamNative.DCAM_PIXELTYPE pixel_type)
        {
            SetProperty(DcamNative.DCAM_IDPROP_IMAGE_PIXELTYPE, (int)pixel_type);
        }

        // --- Capture & Sync ---

        public void StartCapture(DcamNative.DCAMCAP_START mode = DcamNative.DCAMCAP_START.SEQUENCE)
        {
            var err = DcamNative.dcamcap_start(_hDcam, mode);
            if (err != DcamNative.DCAMERR.SUCCESS)
                throw new Exception($"Failed to start capture: {err}");
        }

        public void StopCapture()
        {
            DcamNative.dcamcap_stop(_hDcam);
        }

        public bool WaitForFrame(int timeoutMs = 1000)
        {
            var waitStartParam = new DcamNative.DCAMWAIT_START
            {
                size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(DcamNative.DCAMWAIT_START)),
                eventmask = (int)DcamNative.DCAMWAIT_EVENT.FRAMEREADY,
                timeout = timeoutMs
            };

            var err = DcamNative.dcamwait_start(_hWait, ref waitStartParam);

            if (err == DcamNative.DCAMERR.TIMEOUT) return false;
            if (err != DcamNative.DCAMERR.SUCCESS)
                throw new Exception($"Wait error: {err}");

            return true;
        }

        // Add to DcamCamera.cs

        public void AllocateBuffer(int frameCount)
        {
            var err = DcamNative.dcambuf_alloc(_hDcam, frameCount);
            if (err != DcamNative.DCAMERR.SUCCESS)
                throw new Exception($"Failed to allocate buffer: {err}");
        }

        public void ReleaseBuffer()
        {
            DcamNative.dcambuf_release(_hDcam);
        }

        public void GetTransferInfo(out int frameCount, out int newestFrameIndex)
        {
            var info = new DcamNative.DCAMCAP_TRANSFERINFO
            {
                size = Marshal.SizeOf(typeof(DcamNative.DCAMCAP_TRANSFERINFO))
            };

            var err = DcamNative.dcamcap_transferinfo(_hDcam, ref info);
            if (err != DcamNative.DCAMERR.SUCCESS)
                throw new Exception($"Failed to get transfer info: {err}");

            frameCount = info.nFrameCount;
            newestFrameIndex = info.nNewestFrameIndex;
        }

        public DcamNative.DCAMBUF_FRAME LockFrame(int frameIndex)
        {
            var frame = new DcamNative.DCAMBUF_FRAME
            {
                size = Marshal.SizeOf(typeof(DcamNative.DCAMBUF_FRAME)),
                iFrame = frameIndex
            };

            var err = DcamNative.dcambuf_lockframe(_hDcam, ref frame);
            if (err != DcamNative.DCAMERR.SUCCESS)
                throw new Exception($"Failed to lock frame {frameIndex}: {err}");

            return frame;
        }

        // --- Cleanup ---

        public void Dispose()
        {
            if (_hWait != IntPtr.Zero)
            {
                DcamNative.dcamwait_close(_hWait);
                _hWait = IntPtr.Zero;
            }

            if (_hDcam != IntPtr.Zero)
            {
                DcamNative.dcamdev_close(_hDcam);
                _hDcam = IntPtr.Zero;
            }

            if (_isInitialized)
            {
                DcamNative.dcamapi_uninit();
                _isInitialized = false;
            }
        }
    }
}
