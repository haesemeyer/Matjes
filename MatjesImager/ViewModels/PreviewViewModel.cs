using MatjesImager.Hardware;
using MatjesUtils;
using System;
using System.Collections.Generic;
using System.Text;

namespace MatjesImager.ViewModels
{
    /// <summary>
    /// The first workflow step: live preview with fixed, user-set piezo and sheet positions to find the sample
    /// and bring both sheets into focus. Positions are set with the shell's shared position controls.
    /// The step owns the preview settings (frame rate) that all steps running in preview share, so the shell
    /// starts preview through this step
    /// </summary>
    public class PreviewViewModel : StepViewModel
    {
        #region Constants

        /// <summary>
        /// The lowest preview frame rate that can be set
        /// </summary>
        public const int MinFrameRateHz = 1;

        /// <summary>
        /// The highest preview frame rate that can be set
        /// </summary>
        public const int MaxFrameRateHz = 100;

        /// <summary>
        /// The rate at which frames are written to the display. Faster frame rates only show every n-th frame
        /// </summary>
        public const int DisplayRateHz = 10;

        /// <summary>
        /// The percentile of pixel values mapped to black by auto contrast
        /// </summary>
        private const double _autoContrastLowPercentile = 0.5;

        /// <summary>
        /// The percentile of pixel values mapped to white by auto contrast
        /// </summary>
        private const double _autoContrastHighPercentile = 99.9;

        /// <summary>
        /// Smallest display range set by auto contrast (the minimum range of the shell's display slider)
        /// </summary>
        private const double _minDisplayRange = 10;

        private const string _openHint = "Mark the sample as found once both sheets are in focus";

        #endregion

        #region Members

        private Microscope lightSheet => App.LightSheet;

        private int _frameRateHz = 100;

        private int _requestedFrameRateHz = 100;

        private string _statusMessage = "";

        /// <summary>
        /// Display scaling carried over to the new camera stream when preview restarts
        /// </summary>
        private double _imageScaleMin = 0;

        private double _imageScaleMax = ushort.MaxValue;

        #endregion

        public PreviewViewModel() : base(1, "Find sample", MicroscopeMode.Preview)
        {
            StatusMessage = "Use the position controls to find the sample and bring both sheets into focus.";
            StateHint = _openHint;
        }

        #region Properties

        /// <summary>
        /// The camera frame rate of the running preview. The camera exposes for the whole frame period, so lower rates give brighter images
        /// </summary>
        public int FrameRateHz
        {
            get { return _frameRateHz; }
            private set
            {
                _frameRateHz = value;
                RaisePropertyChanged(nameof(FrameRateHz));
                RaisePropertyChanged(nameof(CanApplyFrameRate));
            }
        }

        /// <summary>
        /// The frame rate entered by the user, applied with ApplyFrameRate
        /// </summary>
        public int RequestedFrameRateHz
        {
            get { return _requestedFrameRateHz; }
            set
            {
                _requestedFrameRateHz = Math.Clamp(value, MinFrameRateHz, MaxFrameRateHz);
                RaisePropertyChanged(nameof(RequestedFrameRateHz));
                RaisePropertyChanged(nameof(RequestedExposureMs));
                RaisePropertyChanged(nameof(CanApplyFrameRate));
            }
        }

        /// <summary>
        /// The exposure time resulting from the requested frame rate
        /// </summary>
        public double RequestedExposureMs => 1000.0 / RequestedFrameRateHz;

        /// <summary>
        /// Indicates whether the requested frame rate differs from the running one
        /// </summary>
        public bool CanApplyFrameRate => RequestedFrameRateHz != FrameRateHz;

        /// <summary>
        /// Indicates whether the user marked the sample as found, which marks the step as done
        /// </summary>
        public bool IsSampleFound
        {
            get { return State == StepState.Done; }
            set
            {
                State = value ? StepState.Done : StepState.Open;
                StateHint = value ? "Sample found" : _openHint;
                RaisePropertyChanged(nameof(IsSampleFound));
            }
        }

        /// <summary>
        /// Instructions and errors shown in the panel
        /// </summary>
        public string StatusMessage
        {
            get { return _statusMessage; }
            private set { _statusMessage = value; RaisePropertyChanged(nameof(StatusMessage)); }
        }

        #endregion

        #region Methods

        /// <summary>
        /// (Re)starts the preview at the current frame rate, keeping the display scaling of the previous camera stream.
        /// Used by the shell to enter preview and by this step to change the frame rate
        /// </summary>
        internal void StartPreview()
        {
            if (lightSheet.Camera != null)
            {
                _imageScaleMin = lightSheet.Camera.ImageScaleMin;
                _imageScaleMax = lightSheet.Camera.ImageScaleMax;
            }
            lightSheet.Stop();
            lightSheet.StartIdleScan(FrameRateHz);
            var camera = lightSheet.Camera!;
            camera.DisplayEvery = Math.Max(1, FrameRateHz / DisplayRateHz);
            camera.ImageScaleMin = _imageScaleMin;
            camera.ImageScaleMax = _imageScaleMax;
        }

        /// <summary>
        /// Restarts the preview at the requested frame rate
        /// </summary>
        public void ApplyFrameRate()
        {
            if (!CanApplyFrameRate)
                return;
            FrameRateHz = RequestedFrameRateHz;
            if (IsInDesignMode || !IsActive)
                return;
            try
            {
                StartPreview();
                StatusMessage = $"Preview running at {FrameRateHz} Hz ({1000.0 / FrameRateHz:F1} ms exposure).";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not restart preview: {ex.Message}";
            }
        }

        /// <summary>
        /// Sets the display scaling from the pixel value distribution of the latest frame
        /// </summary>
        public unsafe void AutoContrast()
        {
            var camera = lightSheet.Camera;
            using var frame = camera?.CopyLatestRawFrame();
            if (camera == null || frame == null)
            {
                StatusMessage = "No frame received yet.";
                return;
            }
            var histogram = new long[ushort.MaxValue + 1];
            for (int y = 0; y < frame.Height; y++)
            {
                ushort* row = frame[0, y];
                for (int x = 0; x < frame.Width; x++)
                    histogram[row[x]]++;
            }
            long numPixels = (long)frame.Width * frame.Height;
            double low = Percentile(histogram, numPixels, _autoContrastLowPercentile);
            double high = Percentile(histogram, numPixels, _autoContrastHighPercentile);
            if (high - low < _minDisplayRange)
                high = Math.Min(low + _minDisplayRange, ushort.MaxValue);
            low = Math.Min(low, high - _minDisplayRange);
            camera.ImageScaleMin = low;
            camera.ImageScaleMax = high;
        }

        /// <summary>
        /// The smallest pixel value at or below which the given percentage of pixels lies
        /// </summary>
        private static int Percentile(long[] histogram, long numPixels, double percent)
        {
            long target = (long)Math.Ceiling(numPixels * percent / 100.0);
            long cumulative = 0;
            for (int value = 0; value < histogram.Length; value++)
            {
                cumulative += histogram[value];
                if (cumulative >= target)
                    return value;
            }
            return histogram.Length - 1;
        }

        #endregion
    }
}
