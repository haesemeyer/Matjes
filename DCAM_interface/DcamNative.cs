//Google GEMINI - fixed large amount of errors and hallucinations based on DCAM .NET sample
using System;
using System.Runtime.InteropServices;

namespace Hamamatsu.Native
{

    public static class DcamNative
    {
        public const string DllName = "dcamapi.dll";

        // Common Error Codes
        public enum DCAMERR : uint
        {
            BUSY = 0x80000101,   // API cannot process in busy state.
            NOTREADY = 0x80000103,   // API requires ready state.
            NOTSTABLE = 0x80000104,   // API requires stable or unstable state.
            UNSTABLE = 0x80000105,   // API does not support in unstable state.
            NOTBUSY = 0x80000107,   // API requires busy state.
            EXCLUDED = 0x80000110,   // some resource is exclusive and already used
            COOLINGTROUBLE = 0x80000302,   // something happens near cooler
            NOTRIGGER = 0x80000303,   // no trigger when necessary. Some camera supports this error.
            TEMPERATURE_TROUBLE = 0x80000304,   // camera warns its temperature
            TOOFREQUENTTRIGGER = 0x80000305,   // input too frequent trigger. Some camera supports this error.
            ABORT = 0x80000102,   // abort process
            TIMEOUT = 0x80000106,   // timeout
            LOSTFRAME = 0x80000301,   // frame data is lost
            MISSINGFRAME_TROUBLE = 0x80000f06,   // frame is lost but reason is low lever driver's bug
            INVALIDIMAGE = 0x80000321,   // hpk format data is invalid data
            NORESOURCE = 0x80000201,   // not enough resource except memory
            NOMEMORY = 0x80000203,   // not enough memory
            NOMODULE = 0x80000204,   // no sub module
            NODRIVER = 0x80000205,   // no driver
            NOCAMERA = 0x80000206,   // no camera
            NOGRABBER = 0x80000207,   // no grabber
            NOCOMBINATION = 0x80000208,   // no combination on registry
            FAILOPEN = 0x80001001,   // DEPRECATED
            FRAMEGRABBER_NEEDS_FIRMWAREUPDATE = 0x80001002,   // need to update frame grabber firmware to use the camera
            INVALIDMODULE = 0x80000211,   // dcam_init() found invalid module
            INVALIDCOMMPORT = 0x80000212,   // invalid serial port
            FAILOPENBUS = 0x81001001,   // the bus or driver are not available
            FAILOPENCAMERA = 0x82001001,   // camera report error during opening
            DEVICEPROBLEM = 0x82001002,   // initialization failed(for maico)
            INVALIDCAMERA = 0x80000806,   // invalid camera
            INVALIDHANDLE = 0x80000807,   // invalid camera handle
            INVALIDPARAM = 0x80000808,   // invalid parameter
            INVALIDVALUE = 0x80000821,   // invalid property value
            OUTOFRANGE = 0x80000822,   // value is out of range
            NOTWRITABLE = 0x80000823,   // the property is not writable
            NOTREADABLE = 0x80000824,   // the property is not readable
            INVALIDPROPERTYID = 0x80000825,   // the property id is invalid
            NEWAPIREQUIRED = 0x80000826,   // old API cannot present the value because only new API need to be used
            WRONGHANDSHAKE = 0x80000827,   // this error happens DCAM get error code from camera unexpectedly
            NOPROPERTY = 0x80000828,   // there is no altenative or influence id, or no more property id
            INVALIDCHANNEL = 0x80000829,   // the property id specifies channel but channel is invalid
            INVALIDVIEW = 0x8000082a,   // the property id specifies channel but channel is invalid
            INVALIDSUBARRAY = 0x8000082b,   // the combination of subarray values are invalid. e.g. DCAM_IDPROP_SUBARRAYHPOS + DCAM_IDPROP_SUBARRAYHSIZE is greater than the number of horizontal pixel of sensor.
            ACCESSDENY = 0x8000082c,   // the property cannot access during this DCAM STATUS
            NOVALUETEXT = 0x8000082d,   // the property does not have value text
            WRONGPROPERTYVALUE = 0x8000082e,   // at least one property value is wrong
            DISHARMONY = 0x80000830,   // the paired camera does not have same parameter
            FRAMEBUNDLESHOULDBEOFF = 0x80000832,   // framebundle mode should be OFF under current property settings
            INVALIDFRAMEINDEX = 0x80000833,   // the frame index is invalid
            INVALIDSESSIONINDEX = 0x80000834,   // the session index is invalid
            NOCORRECTIONDATA = 0x80000838,   // not take the dark and shading correction data yet.
            CHANNELDEPENDENTVALUE = 0x80000839,   // each channel has own property value so can't return overall property value.
            VIEWDEPENDENTVALUE = 0x8000083a,   // each view has own property value so can't return overall property value.
            NODEVICEBUFFER = 0x8000083b,   // the frame count is larger than device momory size on using device memory.
            REQUIREDSNAP = 0x8000083c,   // the capture mode is sequence on using device memory.
            LESSSYSTEMMEMORY = 0x8000083f,   // the sysmte memory size is too small. PC doesn't have enough memory or is limited memory by 32bit OS.
            INVALID_SELECTEDLINES = 0x80000842,   // the combination of selected lines values are invalid. e.g. DCAM_IDPROP_SELECTEDLINES_VPOS + DCAM_IDPROP_SELECTEDLINES_VSIZE is greater than the number of vertical lines of sensor.
            INVALID_REALTIMEGAINCORRECTREGIONS = 0x80000843,   // the combination of hpos and hsize for realtime correct region is invalid. e.g. DCAM_IDPROP_REALTIMECORRECTREGION_HPOS + DCAM_IDPROP_REALTIMECORRECTREGION_HSIZE is grater than the number of horizontal pixel.
            NOTSUPPORT = 0x80000f03,   // camera does not support the function or property with current settings
            FAILREADCAMERA = 0x83001002,   // failed to read data from camera
            FAILWRITECAMERA = 0x83001003,   // failed to write data to the camera
            CONFLICTCOMMPORT = 0x83001004,   // conflict the com port name user set
            OPTICS_UNPLUGGED = 0x83001005,   // Optics part is unplugged so please check it.
            FAILCALIBRATION = 0x83001006,   // fail calibration
            MISMATCH_CONFIGURATION = 0x83001011,   // mismatch between camera output(connection) and frame grabber specs
            INVALIDMEMBER_3 = 0x84000103,   // 3th member variable is invalid value
            INVALIDMEMBER_5 = 0x84000105,   // 5th member variable is invalid value
            INVALIDMEMBER_7 = 0x84000107,   // 7th member variable is invalid value
            INVALIDMEMBER_8 = 0x84000108,   // 7th member variable is invalid value
            INVALIDMEMBER_9 = 0x84000109,   // 9th member variable is invalid value
            FAILEDOPENRECFILE = 0x84001001,   // DCAMREC failed to open the file
            INVALIDRECHANDLE = 0x84001002,   // DCAMREC is invalid handle
            FAILEDWRITEDATA = 0x84001003,   // DCAMREC failed to write the data
            FAILEDREADDATA = 0x84001004,   // DCAMREC failed to read the data
            NOWRECORDING = 0x84001005,   // DCAMREC is recording data now
            WRITEFULL = 0x84001006,   // DCAMREC writes full frame of the session
            ALREADYOCCUPIED = 0x84001007,   // DCAMREC handle is already occupied by other HDCAM
            TOOLARGEUSERDATASIZE = 0x84001008,   // DCAMREC is set the large value to user data size
            INVALIDWAITHANDLE = 0x84002001,   // DCAMWAIT is invalid handle
            NEWRUNTIMEREQUIRED = 0x84002002,   // DCAM Module Version is older than the version that the camera requests
            VERSIONMISMATCH = 0x84002003,   // Camre returns the error on setting parameter to limit version
            RUNAS_FACTORYMODE = 0x84002004,   // Camera is running as a factory mode
            IMAGE_UNKNOWNSIGNATURE = 0x84003001,   // sigunature of image header is unknown or corrupted
            IMAGE_NEWRUNTIMEREQUIRED = 0x84003002,   // version of image header is newer than version that used DCAM supports
            IMAGE_ERRORSTATUSEXIST = 0x84003003,   // image header stands error status
            IMAGE_HEADERCORRUPTED = 0x84004004,   // image header value is strange
            IMAGE_BROKENCONTENT = 0x84004005,   // image content is corrupted
            UNKNOWNMSGID = 0x80000801,   // unknown message id
            UNKNOWNSTRID = 0x80000802,   // unknown string id
            UNKNOWNPARAMID = 0x80000803,   // unkown parameter id
            UNKNOWNBITSTYPE = 0x80000804,   // unknown bitmap bits type
            UNKNOWNDATATYPE = 0x80000805,   // unknown frame data type
            NONE = 0,            // no error, nothing to have done
            INSTALLATIONINPROGRESS = 0x80000f00,   // installation progress
            UNREACH = 0x80000f01,   // internal error
            UNLOADED = 0x80000f04,   // calling after process terminated
            THRUADAPTER = 0x80000f05,
            NOCONNECTION = 0x80000f07,   // HDCAM lost connection to camera
            NOTIMPLEMENT = 0x80000f02,   // not yet implementation
            DELAYEDFRAME = 0x80000f09,   // the frame waiting re-load from hardware buffer with SNAPSHOT(EX) of DEVICEBUFFER MODE
            FAILRELOADFRAME = 0x80000f0a,   // failed to re-load frame from hardware buffer with SNAPSHOT(EX) of DEVICEBUFFER MODE
            CANCELRELOADFRAME = 0x80000f0b,   // cancel to re-load frame from hardware buffer with SNAPSHOT(EX) of DEVICEBUFFER MODE
            DEVICEINITIALIZING = 0xb0000001,
            APIINIT_INITOPTIONBYTES = 0xa4010003,   // DCAMAPI_INIT::initoptionbytes is invalid
            APIINIT_INITOPTION = 0xa4010004,   // DCAMAPI_INIT::initoption is invalid
            INITOPTION_COLLISION_BASE = 0xa401C000,
            INITOPTION_COLLISION_MAX = 0xa401FFFF,
            MISSPROP_TRIGGERSOURCE = 0xE0100110,   // the trigger mode is internal or syncreadout on using device memory.
            SUCCESS = 1,            // no error, general success code, app should check the value is positive
        }

        // Capture Modes
        public enum DCAMCAP_START : int
        {
            SEQUENCE = -1,
            SNAP = 0
        }

        // Wait Event Types
        public enum DCAMWAIT_EVENT : int
        {
            TRANSFERRED = 1,
            FRAMEREADY = 2,
            STOPPED = 16
        }

        // Essential Property IDs (from dcamprop.h)
        // Add more as needed from the Hamamatsu C++ headers
        public const int DCAM_IDPROP_EXPOSURETIME = 0x001F0110;
        public const int DCAM_IDPROP_TRIGGERSOURCE = 0x00100110;
        public const int DCAM_IDPROP_TRIGGERACTIVE = 0x00100120;
        public const int DCAM_IDPROP_TRIGGERMODE = 0x00100210;

        // Binning
        public const int DCAM_IDPROP_BINNING = 0x00401110;

        // Region of Interest (Subarray)
        public const int DCAM_IDPROP_SUBARRAYMODE = 0x00402110; // 1: OFF, 2: ON
        public const int DCAM_IDPROP_SUBARRAYHPOS = 0x04108110; // Horizontal start pixel
        public const int DCAM_IDPROP_SUBARRAYVPOS = 0x04108120; // Vertical start pixel
        public const int DCAM_IDPROP_SUBARRAYHSIZE = 0x04108210; // Width
        public const int DCAM_IDPROP_SUBARRAYVSIZE = 0x04108220; // Height

        // Bit Depth
        public const int DCAM_IDPROP_BITSPERCHANNEL = 0x00420130;

        [StructLayout(LayoutKind.Sequential)]
        public struct DCAMAPI_INIT
        {
            public int size;
            public int iDeviceCount;
            public int reserved;
            public int initoptionbytes;
            public IntPtr initoption;
            public IntPtr guid;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DCAMDEV_OPEN
        {
            public int size;
            public int index;
            public IntPtr hdcam;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DCAMWAIT_OPEN
        {
            public int size;
            public int supportevent;
            public IntPtr hwait;
            public IntPtr hdcam;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DCAMWAIT_START
        {
            public int size;
            public int eventhappened;
            public int eventmask;
            public int timeout;
        }

        // Add to DcamNative.cs

        [StructLayout(LayoutKind.Sequential)]
        public struct DCAMCAP_TRANSFERINFO
        {
            public int size;
            public int iKind;
            public int nNewestFrameIndex;
            public int nFrameCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DCAMBUF_FRAME
        {
            public int size;
            public int iKind;
            public int option;
            public int iFrame;
            public IntPtr buf;
            public int rowbytes;
            public int type;
            public int width;
            public int height;
            public int left;
            public int top;
            public int timestamp_sec;
            public int timestamp_microsec;
            public int framestamp;
            public int camerastamp;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern DCAMERR dcambuf_alloc(IntPtr hdcam, int framecount);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern DCAMERR dcambuf_release(IntPtr hdcam);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern DCAMERR dcamcap_transferinfo(IntPtr hdcam, ref DCAMCAP_TRANSFERINFO param);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern DCAMERR dcambuf_lockframe(IntPtr hdcam, ref DCAMBUF_FRAME param);

        // API Lifecycle
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern DCAMERR dcamapi_init(ref DCAMAPI_INIT param);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern DCAMERR dcamapi_uninit();

        // Device Operations
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern DCAMERR dcamdev_open(ref DCAMDEV_OPEN param);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern DCAMERR dcamdev_close(IntPtr hdcam);

        // Properties
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern DCAMERR dcamprop_setvalue(IntPtr hdcam, int iProp, double fValue);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern DCAMERR dcamprop_getvalue(IntPtr hdcam, int iProp, out double fValue);

        // Capture
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern DCAMERR dcamcap_start(IntPtr hdcam, DCAMCAP_START mode);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern DCAMERR dcamcap_stop(IntPtr hdcam);

        // Waiting/Sync
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern DCAMERR dcamwait_open(ref DCAMWAIT_OPEN param);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern DCAMERR dcamwait_start(IntPtr hwait, ref DCAMWAIT_START param);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern DCAMERR dcamwait_close(IntPtr hwait);
    }
}
