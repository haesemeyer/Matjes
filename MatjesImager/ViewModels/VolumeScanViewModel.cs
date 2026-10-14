using MatjesImager.Hardware;
using MatjesImager.Properties;
using MatjesUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace MatjesImager.ViewModels
{
    /// <summary>
    /// Workflow step 4: sets up the volume scan and tests it. The user sets the stack range, the plane spacing and the volume rate,
    /// from which the frames per volume and the camera frame rate follow (the preview frame rate does not carry over).
    /// A test runs the volume scan while recording the piezo read-back, finds the part of the sawtooth in which the piezo
    /// moves at constant velocity (planes evenly spaced) and proposes the frames (planes) within each volume to keep. The scan keeps running after the
    /// test so that individual planes can be inspected. Accepting stores the configuration for the experiment step
    /// </summary>
    public class VolumeScanViewModel : StepViewModel
    {
        #region Constants

        /// <summary>
        /// The highest camera frame rate a volume scan may use (the Fusion at full frame)
        /// </summary>
        public const int MaxFrameRateHz = 100;

        /// <summary>
        /// Each test records this many volumes in addition to the first, which is discarded while the piezo settles
        /// </summary>
        public const int DefaultTestVolumes = 10;

        /// <summary>
        /// The display shows the selected plane of about this many volumes per second
        /// </summary>
        public const double DisplayRateHz = 10;

        /// <summary>
        /// Interval at which test progress is checked
        /// </summary>
        private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// A test fails if no read-back sample arrives for this long
        /// </summary>
        private static readonly TimeSpan _stallTimeout = TimeSpan.FromSeconds(5);

        private const string _frameNote = "Frames per volume only sets the volume rate. Only frames on the linear part of the piezo sawtooth are kept, " +
            "so each saved volume has fewer planes than frames per volume.";

        #endregion

        #region Members

        private Microscope lightSheet => App.LightSheet;

        private readonly ZCalibrationViewModel _zCalibration;

        private double _startMicrons = 100;

        private double _depthMicrons = 200;

        private double _planeSpacingMicrons = 5;

        private double _requestedVolumeRateHz = 1;

        private int _testVolumes = DefaultTestVolumes;

        private double _tolerancePercent = PiezoLinearity.DefaultPositionTolerance * 100;

        /// <summary>
        /// The read-back of the last completed test and what is needed to analyze it again, null before the first
        /// </summary>
        private (double[] readbackVolts, VolumeScanParams scanParams, int samplesPerFrame, int averaged, double shape)? _lastRecording;

        private string _statusMessage = "";

        private bool _isTesting = false;

        private bool _isAnalyzing = false;

        private double _testProgress = 0;

        /// <summary>
        /// The parameters of the scan this step started, null if it is not running
        /// </summary>
        private VolumeScanParams? _runningParams;

        /// <summary>
        /// The parameters the current result was measured with
        /// </summary>
        private VolumeScanParams? _testedParams;

        private PiezoLinearityResult? _result;

        private int _firstKeptPlane = 0;

        private int _lastKeptPlane = 0;

        private int _displayPlane = 0;

        private Point[] _commandTrace = [];

        private Point[] _readbackTrace = [];

        private VolumeScanConfig? _acceptedConfig;

        private PiezoReadbackRecorder? _recorder;

        private readonly DispatcherTimer _pollTimer;

        /// <summary>
        /// Incremented on each test start and stop so that results of an abandoned test are ignored
        /// </summary>
        private int _testRun = 0;

        private int _lastNumRecorded = 0;

        private DateTime _lastProgressTime;

        /// <summary>
        /// Display scaling carried over between the camera streams of successive scans
        /// </summary>
        private double _imageScaleMin = 0;

        private double _imageScaleMax = ushort.MaxValue;

        #endregion

        public VolumeScanViewModel(ZCalibrationViewModel zCalibration) : base(4, "Volume scan", MicroscopeMode.VolumeScan)
        {
            _zCalibration = zCalibration;
            _zCalibration.CalibrationAccepted += ZCalibration_CalibrationAccepted;
            _pollTimer = new DispatcherTimer { Interval = _pollInterval };
            _pollTimer.Tick += PollTimer_Tick;
            UpdateState();
            StatusMessage = HasCalibration ? "Set the stack and press Test." : "Accept a z calibration (step 3) first.";
        }

        #region Properties

        /// <summary>
        /// Fraction of each sawtooth period spent on the rising (imaging) flank, from the application settings
        /// </summary>
        public static double SawtoothShape => Settings.Default.PiezoSawtoothShape;

        /// <summary>
        /// The travel range of the piezo in microns
        /// </summary>
        public static double PiezoRangeMicrons => Settings.Default.PiezoRangeMicrons;

        /// <summary>
        /// Explains that frames per volume is not the number of saved planes
        /// </summary>
        public static string FramesPerVolumeNote => _frameNote;

        /// <summary>
        /// Indicates whether the z calibration needed to move the sheets with the piezo exists
        /// </summary>
        public bool HasCalibration => _zCalibration.Sheet1Converter != null && _zCalibration.Sheet2Converter != null;

        /// <summary>
        /// The piezo position at the start of the imaging flank in microns
        /// </summary>
        public double StartMicrons
        {
            get { return _startMicrons; }
            set { _startMicrons = Math.Clamp(value, 0, PiezoRangeMicrons); RaisePropertyChanged(nameof(StartMicrons)); ScanSettingsChanged(); }
        }

        /// <summary>
        /// The commanded travel of the piezo on the imaging flank in microns. The linear part covers less than this
        /// </summary>
        public double DepthMicrons
        {
            get { return _depthMicrons; }
            set { _depthMicrons = Math.Clamp(value, 0, PiezoRangeMicrons); RaisePropertyChanged(nameof(DepthMicrons)); ScanSettingsChanged(); }
        }

        /// <summary>
        /// The requested distance between successive planes in microns
        /// </summary>
        public double PlaneSpacingMicrons
        {
            get { return _planeSpacingMicrons; }
            set { _planeSpacingMicrons = Math.Max(0.1, value); RaisePropertyChanged(nameof(PlaneSpacingMicrons)); ScanSettingsChanged(); }
        }

        /// <summary>
        /// The requested volume rate. The actual rate differs slightly since the camera frame rate is a whole number
        /// </summary>
        public double RequestedVolumeRateHz
        {
            get { return _requestedVolumeRateHz; }
            set { _requestedVolumeRateHz = Math.Max(0.01, value); RaisePropertyChanged(nameof(RequestedVolumeRateHz)); ScanSettingsChanged(); }
        }

        /// <summary>
        /// The number of volumes analyzed by a test (an extra first volume is discarded)
        /// </summary>
        public int TestVolumes
        {
            get { return _testVolumes; }
            set { _testVolumes = Math.Clamp(value, 1, 100); RaisePropertyChanged(nameof(TestVolumes)); }
        }

        /// <summary>
        /// The largest deviation of a plane from an evenly spaced grid still counted as linear, in percent of the plane spacing.
        /// Changing it analyzes the last test again, which proposes new planes to keep
        /// </summary>
        public double TolerancePercent
        {
            get { return _tolerancePercent; }
            set
            {
                double tolerance = Math.Clamp(value, 1, 100);
                bool changed = tolerance != _tolerancePercent;
                _tolerancePercent = tolerance;
                RaisePropertyChanged(nameof(TolerancePercent));
                if (changed)
                    Reanalyze();
            }
        }

        /// <summary>
        /// The frames per volume that give the requested plane spacing: the piezo covers the depth during the
        /// imaging flank, which lasts SawtoothShape of the volume
        /// </summary>
        public int FramesPerVolume
        {
            get
            {
                if (DepthMicrons <= 0)
                    return 1;
                return Math.Max(2, (int)Math.Round(DepthMicrons / (SawtoothShape * PlaneSpacingMicrons)));
            }
        }

        /// <summary>
        /// The camera frame rate of the volume scan, the nearest whole number to frames per volume times volume rate
        /// </summary>
        public int FrameRateHz => Math.Max(1, (int)Math.Round(FramesPerVolume * RequestedVolumeRateHz));

        /// <summary>
        /// The volume rate resulting from the whole-number frame rate
        /// </summary>
        public double VolumeRateHz => (double)FrameRateHz / FramesPerVolume;

        /// <summary>
        /// The plane spacing the commanded sawtooth gives with the chosen frames per volume
        /// </summary>
        public double CommandedPlaneSpacingMicrons => FramesPerVolume < 1 ? 0 : DepthMicrons / (SawtoothShape * FramesPerVolume);

        /// <summary>
        /// Reason why the current settings cannot be scanned, empty if they can
        /// </summary>
        public string SettingsProblem
        {
            get
            {
                if (!HasCalibration)
                    return "Needs an accepted z calibration (step 3).";
                if (DepthMicrons <= 0)
                    return "The depth has to be larger than 0.";
                if (StartMicrons + DepthMicrons > PiezoRangeMicrons)
                    return $"The stack ends at {StartMicrons + DepthMicrons:F0} um, beyond the piezo range of {PiezoRangeMicrons:F0} um.";
                if (FrameRateHz > MaxFrameRateHz)
                    return $"Needs {FrameRateHz} frames per second, the camera allows at most {MaxFrameRateHz}. Lower the volume rate, increase the plane spacing or reduce the depth.";
                return "";
            }
        }

        /// <summary>
        /// Indicates whether a test can be started with the current settings
        /// </summary>
        public bool CanTest => SettingsProblem == "" && !IsAnalyzing;

        /// <summary>
        /// Indicates whether a volume scan started by this step is running
        /// </summary>
        public bool IsScanning => _runningParams != null;

        /// <summary>
        /// Indicates whether the read-back of a test is being recorded
        /// </summary>
        public bool IsTesting
        {
            get { return _isTesting; }
            private set { _isTesting = value; RaisePropertyChanged(nameof(IsTesting)); }
        }

        /// <summary>
        /// Indicates whether a recorded test is being analyzed
        /// </summary>
        public bool IsAnalyzing
        {
            get { return _isAnalyzing; }
            private set { _isAnalyzing = value; RaisePropertyChanged(nameof(IsAnalyzing)); RaisePropertyChanged(nameof(CanTest)); }
        }

        /// <summary>
        /// Fraction of the test recording done, 0 to 1
        /// </summary>
        public double TestProgress
        {
            get { return _testProgress; }
            private set { _testProgress = value; RaisePropertyChanged(nameof(TestProgress)); }
        }

        /// <summary>
        /// Guidance and error messages for the user
        /// </summary>
        public string StatusMessage
        {
            get { return _statusMessage; }
            private set { _statusMessage = value; RaisePropertyChanged(nameof(StatusMessage)); }
        }

        /// <summary>
        /// The result of the last test, null before the first
        /// </summary>
        public PiezoLinearityResult? Result
        {
            get { return _result; }
            private set
            {
                _result = value;
                RaisePropertyChanged(nameof(Result));
                RaisePropertyChanged(nameof(HasValidResult));
                RaisePropertyChanged(nameof(MeasuredPlaneSpacingMicrons));
                RaisePropertyChanged(nameof(LinearWindowStartFrames));
                RaisePropertyChanged(nameof(LinearWindowEndFrames));
                RaiseKeptPlanesChanged();
            }
        }

        /// <summary>
        /// Indicates whether the last test found a linear window
        /// </summary>
        public bool HasValidResult => Result?.IsValid == true;

        /// <summary>
        /// The plane spacing measured from the read-back (slope of the fitted plane grid), NaN without a result
        /// </summary>
        public double MeasuredPlaneSpacingMicrons => HasValidResult && _testedParams != null
            ? Result!.VelocityMicronsPerSample * ReadbackSamplesPerFrame : double.NaN;

        /// <summary>
        /// Start of the linear window in frames from volume start, NaN without a result
        /// </summary>
        public double LinearWindowStartFrames => HasValidResult ? (double)Result!.WindowStartSample / ReadbackSamplesPerFrame : double.NaN;

        /// <summary>
        /// End of the linear window in frames from volume start, NaN without a result
        /// </summary>
        public double LinearWindowEndFrames => HasValidResult ? (Result!.WindowEndSample + 1.0) / ReadbackSamplesPerFrame : double.NaN;

        /// <summary>
        /// The cycle-averaged piezo command over one volume as (frames, um) for plotting
        /// </summary>
        public Point[] CommandTrace
        {
            get { return _commandTrace; }
            private set { _commandTrace = value; RaisePropertyChanged(nameof(CommandTrace)); }
        }

        /// <summary>
        /// The cycle-averaged piezo read-back over one volume as (frames, um) for plotting
        /// </summary>
        public Point[] ReadbackTrace
        {
            get { return _readbackTrace; }
            private set { _readbackTrace = value; RaisePropertyChanged(nameof(ReadbackTrace)); }
        }

        /// <summary>
        /// The highest plane index of the running or tested scan
        /// </summary>
        public int MaxPlane => Math.Max(0, (_runningParams?.FramesPerVolume ?? _testedParams?.FramesPerVolume ?? 1) - 1);

        /// <summary>
        /// The first frame within each volume to keep. Proposed by the test, adjustable by the user
        /// </summary>
        public int FirstKeptPlane
        {
            get { return _firstKeptPlane; }
            set
            {
                _firstKeptPlane = Math.Clamp(value, 0, MaxPlane);
                if (_lastKeptPlane < _firstKeptPlane)
                    _lastKeptPlane = _firstKeptPlane;
                RaiseKeptPlanesChanged();
            }
        }

        /// <summary>
        /// The last frame within each volume to keep (inclusive). Proposed by the test, adjustable by the user
        /// </summary>
        public int LastKeptPlane
        {
            get { return _lastKeptPlane; }
            set
            {
                _lastKeptPlane = Math.Clamp(value, 0, MaxPlane);
                if (_firstKeptPlane > _lastKeptPlane)
                    _firstKeptPlane = _lastKeptPlane;
                RaiseKeptPlanesChanged();
            }
        }

        /// <summary>
        /// The number of planes saved per volume
        /// </summary>
        public int NumKeptPlanes => HasValidResult ? LastKeptPlane - FirstKeptPlane + 1 : 0;

        /// <summary>
        /// The measured depth covered by the kept planes in microns
        /// </summary>
        public double KeptDepthMicrons => HasValidResult
            ? Result!.PlanePositionsMicrons[LastKeptPlane] - Result.PlanePositionsMicrons[FirstKeptPlane] : double.NaN;

        /// <summary>
        /// Indicates that the kept planes reach outside the linear window, where plane spacing is not uniform
        /// </summary>
        public bool KeptPlanesOutsideLinearWindow => HasValidResult && (FirstKeptPlane < Result!.FirstLinearPlane || LastKeptPlane > Result.LastLinearPlane);

        /// <summary>
        /// Kept planes as (start, end) in frames from volume start for plotting
        /// </summary>
        public double KeptStartFrames => HasValidResult ? FirstKeptPlane : double.NaN;

        public double KeptEndFrames => HasValidResult ? LastKeptPlane + 1 : double.NaN;

        /// <summary>
        /// The plane shown on the display while the scan runs
        /// </summary>
        public int DisplayPlane
        {
            get { return _displayPlane; }
            set
            {
                _displayPlane = Math.Clamp(value, 0, MaxPlane);
                RaisePropertyChanged(nameof(DisplayPlane));
                RaisePropertyChanged(nameof(DisplayPlaneMicrons));
                if (IsScanning && lightSheet.Camera != null)
                    lightSheet.Camera.DisplayPlane = _displayPlane;
            }
        }

        /// <summary>
        /// The measured piezo position of the displayed plane, NaN without a result
        /// </summary>
        public double DisplayPlaneMicrons => HasValidResult && DisplayPlane < Result!.PlanePositionsMicrons.Length
            ? Result.PlanePositionsMicrons[DisplayPlane] : double.NaN;

        /// <summary>
        /// Indicates whether the tested configuration can be accepted: the test found a linear window and the settings did not change since
        /// </summary>
        public bool CanAccept => HasValidResult && _testedParams != null && SettingsMatch(_testedParams.Value) && !IsAnalyzing;

        /// <summary>
        /// The accepted configuration, null until one was accepted
        /// </summary>
        public VolumeScanConfig? AcceptedConfig
        {
            get { return _acceptedConfig; }
            private set { _acceptedConfig = value; RaisePropertyChanged(nameof(AcceptedConfig)); }
        }

        /// <summary>
        /// Raised when the user accepts a configuration. AcceptedConfig holds it
        /// </summary>
        public event EventHandler? ConfigAccepted;

        /// <summary>
        /// The number of read-back samples per camera frame
        /// </summary>
        private static int ReadbackSamplesPerFrame => Settings.Default.SamplesPerFrame / Microscope.HardwareReadSamplesToAverage;

        #endregion

        #region Methods

        /// <summary>
        /// (Re)starts the volume scan with the current settings and records the piezo read-back for the test
        /// </summary>
        public void StartTest()
        {
            if (IsInDesignMode)
                return;
            if (!CanTest)
            {
                StatusMessage = SettingsProblem;
                return;
            }
            StopScan();
            VolumeScanParams scanParams;
            try
            {
                scanParams = BuildParams();
            }
            catch (ArgumentException ex)
            {
                StatusMessage = ex.Message;
                return;
            }
            int samplesPerVolume = scanParams.FramesPerVolume * ReadbackSamplesPerFrame;
            var recorder = new PiezoReadbackRecorder((TestVolumes + 1) * samplesPerVolume);
            try
            {
                lightSheet.StartVolumeScan(scanParams, recorder);
            }
            catch (Exception ex)
            {
                recorder.Dispose();
                StatusMessage = $"Could not start the volume scan: {ex.Message}";
                return;
            }
            _recorder = recorder;
            _runningParams = scanParams;
            // The previous result belongs to other scan settings
            _testedParams = null;
            _lastRecording = null;
            CommandTrace = [];
            ReadbackTrace = [];
            Result = null;
            _testRun++;
            _lastNumRecorded = 0;
            _lastProgressTime = DateTime.Now;
            var camera = lightSheet.Camera!;
            camera.ImageScaleMin = _imageScaleMin;
            camera.ImageScaleMax = _imageScaleMax;
            RaisePropertyChanged(nameof(IsScanning));
            RaisePropertyChanged(nameof(MaxPlane));
            DisplayPlane = _displayPlane;
            TestProgress = 0;
            IsTesting = true;
            _pollTimer.Start();
            StatusMessage = $"Testing: recording {TestVolumes + 1} volumes at {scanParams.CameraFrameRate} frames per second " +
                $"({(TestVolumes + 1) / VolumeRateHz:F1} s).";
        }

        /// <summary>
        /// Stops the volume scan this step started
        /// </summary>
        public void StopScan()
        {
            AbandonTest();
            if (!IsScanning)
                return;
            RememberDisplayScaling();
            if (!IsInDesignMode && lightSheet.Mode == MicroscopeMode.VolumeScan)
                lightSheet.Stop();
            _runningParams = null;
            RaisePropertyChanged(nameof(IsScanning));
        }

        /// <summary>
        /// Sets the display scaling from the pixel value distribution of the latest frame
        /// </summary>
        public void AutoContrast()
        {
            var camera = lightSheet.Camera;
            using var frame = camera?.CopyLatestRawFrame();
            if (camera == null || frame == null)
            {
                StatusMessage = "No frame received yet.";
                return;
            }
            var (low, high) = PreviewViewModel.AutoContrastRange(frame);
            camera.ImageScaleMin = low;
            camera.ImageScaleMax = high;
        }

        /// <summary>
        /// Stores the tested configuration with the chosen planes and raises ConfigAccepted
        /// </summary>
        public void Accept()
        {
            if (!CanAccept)
            {
                StatusMessage = HasValidResult ? "The settings changed since the test. Test again before accepting." : "Run a successful test first.";
                return;
            }
            AcceptedConfig = new VolumeScanConfig(_testedParams!.Value, FirstKeptPlane, LastKeptPlane, Result!.PlanePositionsMicrons);
            State = StepState.Done;
            StateHint = $"Accepted: {NumKeptPlanes} planes per volume at {VolumeRateHz:F2} Hz";
            StatusMessage = $"Accepted {NumKeptPlanes} planes per volume ({KeptDepthMicrons:F0} um) at {VolumeRateHz:F2} volumes per second.";
            ConfigAccepted?.Invoke(this, EventArgs.Empty);
        }

        private VolumeScanParams BuildParams()
        {
            return new VolumeScanParams(FrameRateHz, FramesPerVolume, StartMicrons, DepthMicrons, VolumeScanType.ScanOnly,
                _zCalibration.Sheet1Converter!, _zCalibration.Sheet2Converter!);
        }

        /// <summary>
        /// Indicates whether the current settings would produce the given scan
        /// </summary>
        private bool SettingsMatch(VolumeScanParams p)
        {
            return p.CameraFrameRate == FrameRateHz && p.FramesPerVolume == FramesPerVolume && p.StartMicrons == StartMicrons
                && p.DepthMicrons == DepthMicrons && ReferenceEquals(p.ZConverterSheet1, _zCalibration.Sheet1Converter)
                && ReferenceEquals(p.ZConverterSheet2, _zCalibration.Sheet2Converter);
        }

        /// <summary>
        /// Stops polling and discards the recording of a running test. The scan itself keeps running
        /// </summary>
        private void AbandonTest()
        {
            _pollTimer.Stop();
            _testRun++;
            _recorder = null; // The microscope stops and releases the recorder together with the scan
            IsTesting = false;
        }

        private async void PollTimer_Tick(object? sender, EventArgs e)
        {
            var recorder = _recorder;
            if (recorder == null || _runningParams == null)
            {
                _pollTimer.Stop();
                return;
            }
            VolumeScanParams scanParams = _runningParams.Value;
            TestProgress = (double)recorder.NumRecorded / recorder.SamplesToRecord;
            var readError = lightSheet.HardwareRead?.ReadError;
            if (readError != null || recorder.HasGap)
            {
                AbandonTest();
                StatusMessage = "Test failed: piezo read-back samples were lost" + (readError != null ? $" ({readError.Message})." : ".") + " The scan keeps running, test again.";
                return;
            }
            if (recorder.NumRecorded != _lastNumRecorded)
            {
                _lastNumRecorded = recorder.NumRecorded;
                _lastProgressTime = DateTime.Now;
            }
            else if (DateTime.Now - _lastProgressTime > _stallTimeout)
            {
                AbandonTest();
                StatusMessage = "Test failed: no piezo read-back samples arrived. Check the analog input wiring and settings.";
                return;
            }
            if (!recorder.IsComplete)
                return;

            // Recording done: analyze off the UI thread while the scan keeps running for inspection
            _pollTimer.Stop();
            IsTesting = false;
            int run = _testRun;
            double[] readbackVolts = recorder.GetSamples()!;
            _recorder = null;
            IsAnalyzing = true;
            StatusMessage = "Analyzing the piezo read-back...";
            PiezoLinearityResult result;
            int samplesPerFrame = ReadbackSamplesPerFrame;
            int averaged = Microscope.HardwareReadSamplesToAverage;
            double shape = SawtoothShape;
            double tolerance = TolerancePercent / 100;
            try
            {
                result = await System.Threading.Tasks.Task.Run(() => Analyze(readbackVolts, scanParams, samplesPerFrame, averaged, shape, tolerance));
            }
            catch (Exception ex)
            {
                result = new PiezoLinearityResult { IsValid = false, Message = $"Analysis failed: {ex.Message}" };
            }
            finally
            {
                IsAnalyzing = false;
            }
            if (run != _testRun)
                return; // A new test was started or the step was left in the meantime
            _lastRecording = (readbackVolts, scanParams, samplesPerFrame, averaged, shape);
            ApplyResult(result, scanParams);
            if (tolerance != TolerancePercent / 100)
                Reanalyze(); // The tolerance changed during the analysis
        }

        /// <summary>
        /// Rebuilds the command waveform the scan head generated, converts the read-back to microns and finds the linear window
        /// </summary>
        private static PiezoLinearityResult Analyze(double[] readbackVolts, VolumeScanParams scanParams, int samplesPerFrame, int averaged, double shape,
            double tolerance)
        {
            int n = readbackVolts.Length;
            int samplesPerVolume = scanParams.FramesPerVolume * samplesPerFrame;
            long aoSamplesPerVolume = (long)samplesPerVolume * averaged;
            var command = new double[n];
            var readback = new double[n];
            for (int i = 0; i < n; i++)
            {
                // Read-back sample i is the average over z samples i * averaged ... (i + 1) * averaged - 1, same as in ScanControl.GenerateScanZBuffer
                double sum = 0;
                for (int j = 0; j < averaged; j++)
                {
                    long aoSample = (long)i * averaged + j;
                    double phase = (double)(aoSample % aoSamplesPerVolume) / aoSamplesPerVolume;
                    sum += scanParams.StartMicrons + ScanWaveforms.Sawtooth(phase, shape) * scanParams.DepthMicrons;
                }
                command[i] = sum / averaged;
                // NOTE: Assumes the controller's position monitor uses the same volts-to-microns scale as the command input
                readback[i] = PiezoConfig.VoltsToMicrons(readbackVolts[i]);
            }
            return PiezoLinearity.Analyze(command, readback, samplesPerVolume, samplesPerFrame, shape, positionTolerance: tolerance);
        }

        /// <summary>
        /// Analyzes the last test's read-back again with the current tolerance. Does nothing while a test is recorded or
        /// analyzed (it picks up the tolerance itself) or before the first test
        /// </summary>
        private void Reanalyze()
        {
            if (_lastRecording == null || IsTesting || IsAnalyzing)
                return;
            var (readbackVolts, scanParams, samplesPerFrame, averaged, shape) = _lastRecording.Value;
            PiezoLinearityResult result;
            try
            {
                result = Analyze(readbackVolts, scanParams, samplesPerFrame, averaged, shape, TolerancePercent / 100);
            }
            catch (Exception ex)
            {
                result = new PiezoLinearityResult { IsValid = false, Message = $"Analysis failed: {ex.Message}" };
            }
            ApplyResult(result, scanParams, keepDisplayPlane: true);
        }

        private void ApplyResult(PiezoLinearityResult result, VolumeScanParams scanParams, bool keepDisplayPlane = false)
        {
            _testedParams = scanParams;
            RaisePropertyChanged(nameof(MaxPlane));
            double framesPerSample = 1.0 / ReadbackSamplesPerFrame;
            CommandTrace = ToTrace(result.CycleAverageCommand, framesPerSample);
            ReadbackTrace = ToTrace(result.CycleAverageReadback, framesPerSample);
            if (result.IsValid)
            {
                _firstKeptPlane = result.FirstLinearPlane;
                _lastKeptPlane = result.LastLinearPlane;
            }
            Result = result;
            if (result.IsValid)
            {
                if (!keepDisplayPlane)
                    DisplayPlane = (result.FirstLinearPlane + result.LastLinearPlane) / 2;
                StatusMessage = result.Message + " Check the planes and accept, or change the settings and test again.";
            }
            else
            {
                StatusMessage = "Test failed: " + result.Message;
            }
            UpdateState();
        }

        /// <summary>
        /// Converts a per-sample trace into plot points, thinned out to keep plotting cheap
        /// </summary>
        private static Point[] ToTrace(double[] values, double xPerSample, int maxPoints = 1000)
        {
            if (values.Length == 0)
                return [];
            int step = Math.Max(1, values.Length / maxPoints);
            var points = new List<Point>();
            for (int i = 0; i < values.Length; i += step)
                points.Add(new Point(i * xPerSample, values[i]));
            return points.ToArray();
        }

        private void RememberDisplayScaling()
        {
            if (IsInDesignMode || lightSheet.Camera == null)
                return;
            _imageScaleMin = lightSheet.Camera.ImageScaleMin;
            _imageScaleMax = lightSheet.Camera.ImageScaleMax;
        }

        private void RaiseKeptPlanesChanged()
        {
            RaisePropertyChanged(nameof(FirstKeptPlane));
            RaisePropertyChanged(nameof(LastKeptPlane));
            RaisePropertyChanged(nameof(NumKeptPlanes));
            RaisePropertyChanged(nameof(KeptDepthMicrons));
            RaisePropertyChanged(nameof(KeptPlanesOutsideLinearWindow));
            RaisePropertyChanged(nameof(KeptStartFrames));
            RaisePropertyChanged(nameof(KeptEndFrames));
            RaisePropertyChanged(nameof(DisplayPlaneMicrons));
            RaisePropertyChanged(nameof(CanAccept));
        }

        /// <summary>
        /// Updates the derived values when a setting changed and flags an accepted configuration that no longer matches
        /// </summary>
        private void ScanSettingsChanged()
        {
            RaisePropertyChanged(nameof(FramesPerVolume));
            RaisePropertyChanged(nameof(FrameRateHz));
            RaisePropertyChanged(nameof(VolumeRateHz));
            RaisePropertyChanged(nameof(CommandedPlaneSpacingMicrons));
            RaisePropertyChanged(nameof(SettingsProblem));
            RaisePropertyChanged(nameof(CanTest));
            RaisePropertyChanged(nameof(CanAccept));
            UpdateState();
        }

        /// <summary>
        /// Sets the step list marker: done while the accepted configuration matches the settings and calibration
        /// </summary>
        private void UpdateState()
        {
            if (!HasCalibration)
            {
                State = AcceptedConfig != null ? StepState.NeedsAttention : StepState.Open;
                StateHint = "Needs an accepted z calibration (step 3)";
            }
            else if (AcceptedConfig == null)
            {
                State = StepState.Open;
                StateHint = "Test the volume scan and accept it";
            }
            else if (!SettingsMatch(AcceptedConfig.ScanParams))
            {
                State = StepState.NeedsAttention;
                StateHint = "Settings or z calibration changed since the scan was accepted. Test and accept again";
            }
            else
            {
                State = StepState.Done;
                StateHint = $"Accepted: {AcceptedConfig.NumPlanes} planes per volume";
            }
        }

        private void ZCalibration_CalibrationAccepted(object? sender, EventArgs e)
        {
            RaisePropertyChanged(nameof(HasCalibration));
            ScanSettingsChanged();
        }

        protected override void OnActivated()
        {
            RaisePropertyChanged(nameof(HasCalibration));
            ScanSettingsChanged();
            if (SettingsProblem != "")
                StatusMessage = SettingsProblem;
        }

        /// <summary>
        /// The shell stops the scan when it switches the microscope into the next step's mode. Only the test is abandoned here
        /// </summary>
        protected override void OnDeactivated()
        {
            RememberDisplayScaling();
            if (IsTesting)
                StatusMessage = "The test was interrupted. Test again.";
            AbandonTest();
            _runningParams = null;
            RaisePropertyChanged(nameof(IsScanning));
        }

        #endregion

        #region IDisposable

        override protected void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                Deactivate();
                _pollTimer.Stop();
                _zCalibration.CalibrationAccepted -= ZCalibration_CalibrationAccepted;
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
