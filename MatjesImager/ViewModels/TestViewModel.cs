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
        private Microscope? lightSheet => (App.Current as App).LightSheet;

        public EZImageSource? CamDisplay
        {
            get { return lightSheet?.Camera?.Display; }
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

        public ScanControl? Scanhead => lightSheet?.ScanHead;

        public CameraStream? Camera => lightSheet?.Camera;

        public TestViewModel() {
            if (IsInDesignMode)
                return;
            lightSheet?.StartIdleScan(100);
        }

        public void StartZScan()
        {
            lightSheet?.Stop();
            var scanParams = new VolumeScanParams(100, 100, 5, 400, VolumeScanType.ScanOnly, new LinearZConverter(-4.0 / 450, 2, 5, -5), new LinearZConverter(-4.0 / 450, 2, 5, -5));
            lightSheet?.StartVolumeScan(scanParams);
        }

        override protected void Dispose(bool disposing)
        {
            lightSheet?.Stop();
        }
    }
    }
