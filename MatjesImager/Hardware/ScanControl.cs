using MatjesImager.Properties;
using MatjesUtils;
using NationalInstruments.DAQmx;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.RightsManagement;
using System.Text;

namespace MatjesImager.Hardware
{
    /// <summary>
    /// Class to represent all scan, objective, and camera control operations, which need to be synchronized
    /// We make the (reasonable) assumption that the volume rate will always be a divisor of the frame rate
    /// </summary>
    public class ScanControl : PropertyChangeNotification, IDisposable
    {
        #region Members
        //Each sheet is created by oscillating the X-mirror of the corresponding scanner between left and right volts

        /// <summary>
        /// The voltage of the left edge of the first sheet
        /// </summary>
        private double _sheet1LeftVolts;

        /// <summary>
        /// The voltage of the right edge of the first sheet
        /// </summary>
        private double _sheet1RightVolts;

        /// <summary>
        /// The voltage of the left edge of the second sheet
        /// </summary>
        private double _sheet2LeftVolts;

        /// <summary>
        /// The voltage of the right edge of the second sheet
        /// </summary>
        private double _sheet2RightVolts;

        // During preview/idle the z-positions of each scanner and the Piezo are independent and set to a fixed voltage

        /// <summary>
        /// The fixed z-position of the first sheet
        /// </summary>
        private double _z1_fixed;

        /// <summary>
        /// The fixed z-position of the second sheet
        /// </summary>
        private double _z2_fixed;

        /// <summary>
        /// The fixed piezo position (in volts)
        /// </summary>
        private double _piezo_fixed_volts;

        /// <summary>
        /// Indicates that a scan thread is currently running
        /// </summary>
        private bool _isRunning = false;

        /// <summary>
        /// Token source for scan thread control
        /// </summary>
        private CancellationTokenSource? _cancellationTokenSource;

        private bool disposedValue;

        // NI-DAQmx Tasks

        /// <summary>
        /// Task for the counter that controls the Fusion
        /// </summary>
        private NationalInstruments.DAQmx.Task? _counterTask;

        /// <summary>
        /// Task that controls the x-mirrors of both scan-heads to generate the sheet
        /// </summary>
        private NationalInstruments.DAQmx.Task? _aoTask_sheet;

        /// <summary>
        /// Task that controls the z-positions of both scan-heads and the piezo
        /// </summary>
        private NationalInstruments.DAQmx.Task? _aoTask_Z;

        /// <summary>
        /// The number of sweeps on the sheet generating mirrors to perform per camera frame
        /// </summary>
        private int _sweepsPerFrame;

        /// <summary>
        /// Sample writer object for sheet task
        /// </summary>
        private AnalogMultiChannelWriter? _sheetWriter;

        /// <summary>
        /// Sample writer objectg for z control task
        /// </summary>
        private AnalogMultiChannelWriter? _zWriter;

        /// <summary>
        /// The calculated current sample rate across all tasks and devices
        /// </summary>
        private double _sampleRate;

        /// <summary>
        /// Counts the total number of generated samples for the sheet waveforms
        /// </summary>
        private long _sheet_sample_index = 0;

        /// <summary>
        /// Counts the total number of generated samples for the z waveforms
        /// </summary>
        private long _z_sample_index = 0;

        /// <summary>
        /// Determines the total number of samples to generate for the ao waveforms
        /// in each generation cycle - this number can be independent of wave periods
        /// </summary>
        private int _samples_to_generate;

        /// <summary>
        /// The number of samples within one period of our z-sweep
        /// </summary>
        private int _z_period_samples;

        // Waveform parameters are loaded from application settings when a scan starts (see LoadWaveformSettings)
        // so that they cannot change while tasks are running

        /// <summary>
        /// Smoothing window of the sheet triangle turn-arounds as a fraction of the sweep period
        /// </summary>
        private double _sheetTurnAround;

        /// <summary>
        /// Fraction of each piezo sawtooth period spent on the rising flank
        /// </summary>
        private double _piezoSawtoothShape;

        /// <summary>
        /// The number of ao samples to generate for each camera frame
        /// </summary>
        private int _samplesPerFrame;

        /// <summary>
        /// The buffer size on our AO tasks will be generated_samples x _buffer_mult in size
        /// </summary>
        private int _buffer_mult;


        #endregion

        public ScanControl()
        {
            Sheet1LeftVolts = -1;
            Sheet2LeftVolts = -0.5;
            Sheet1RightVolts = 1;
            Sheet2RightVolts = 0.5;
            Z1_Fixed = 0;
            Z2_Fixed = 0;
            Piezo_Fixed_Microns = 0;
        }

        #region Properties

        /// <summary>
        /// The voltage of the left edge of the first sheet
        /// </summary>
        public double Sheet1LeftVolts
        {
            get { return _sheet1LeftVolts; }
            set { _sheet1LeftVolts = value; RaisePropertyChanged(nameof(Sheet1LeftVolts)); }
        }

        /// <summary>
        /// The voltage of the right edge of the first sheet
        /// </summary>
        public double Sheet1RightVolts
        {
            get { return _sheet1RightVolts; }
            set { _sheet1RightVolts = value; RaisePropertyChanged(nameof(Sheet1RightVolts)); }
        }

        /// <summary>
        /// The voltage of the left edge of the second sheet
        /// </summary>
        public double Sheet2LeftVolts
        {
            get { return _sheet2LeftVolts; }
            set { _sheet2LeftVolts = value; RaisePropertyChanged(nameof(Sheet2LeftVolts)); }
        }

        /// <summary>
        /// The voltage of the right edge of the second sheet
        /// </summary>
        public double Sheet2RightVolts
        {
            get { return _sheet2RightVolts; }
            set { _sheet2RightVolts = value; RaisePropertyChanged(nameof(Sheet2RightVolts)); }
        }

        /// <summary>
        /// The fixed z-position of the first sheet
        /// </summary>
        public double Z1_Fixed
        {
            get { return _z1_fixed; }
            set { _z1_fixed = value; RaisePropertyChanged(nameof(Z1_Fixed)); }
        }

        /// <summary>
        /// The fixed z-position of the second sheet
        /// </summary>
        public double Z2_Fixed
        {
            get { return _z2_fixed; }
            set { _z2_fixed = value; RaisePropertyChanged(nameof(Z2_Fixed)); }
        }

        /// <summary>
        /// The fixed piezo position (in microns)
        /// </summary>
        public double Piezo_Fixed_Microns
        {
            get { return _piezo_fixed_volts / Settings.Default.PiezoMaxVolts * Settings.Default.PiezoRangeMicrons; }
            set { _piezo_fixed_volts = value / Settings.Default.PiezoRangeMicrons * Settings.Default.PiezoMaxVolts; RaisePropertyChanged(nameof(Piezo_Fixed_Microns)); }
        }

        /// <summary>
        /// The full travel of the piezo (in microns)
        /// </summary>
        public double PiezoRangeMicrons
        {
            get { return Settings.Default.PiezoRangeMicrons; }
        }

        // Physical channel names assembled from the board and channel names in the application settings
        private static string Sheet1Channel => $"{Settings.Default.SheetBoard}/{Settings.Default.Sheet1Channel}";
        private static string Sheet2Channel => $"{Settings.Default.SheetBoard}/{Settings.Default.Sheet2Channel}";
        private static string Z1Channel => $"{Settings.Default.ZAndCamBoard}/{Settings.Default.Z1Channel}";
        private static string Z2Channel => $"{Settings.Default.ZAndCamBoard}/{Settings.Default.Z2Channel}";
        private static string PiezoChannel => $"{Settings.Default.ZAndCamBoard}/{Settings.Default.PiezoChannel}";
        private static string CounterChannel => $"{Settings.Default.ZAndCamBoard}/{Settings.Default.CameraTriggerCounter}";
        private static string CounterOutputTerminal => $"/{Settings.Default.ZAndCamBoard}/{Settings.Default.CameraTriggerTerminal}";
        private static string CounterInternalOutput => $"/{Settings.Default.ZAndCamBoard}/{Settings.Default.CameraTriggerCounter}InternalOutput";

        #endregion

        #region ThreadProcs

        private void IdleLoop(CancellationToken token)
        {
            double[,] sheetBuffer;
            double[,] zBuffer;
            while (_isRunning && !token.IsCancellationRequested)
            {
                // To prevent buffer over-runs, which can lead to discontinuities, we wait until space for a full set of samples
                // is available in the bufffer, which we have configured to be twice the generated sample size.
                // Note: Our Z and sheet tasks are not synchronized. Therefore, for experimental loops we need to separate the threads and wait
                // on available samples independently to avoid jitter. However, for the idle loop we simply wait on the sheet and allow for jitter
                // on z in cases where the task is lagging behind in generation
                try { WaitForAOSpace(_aoTask_sheet.Stream, token); }
                catch (OperationCanceledException) { break;  }
                    
                sheetBuffer = GenerateSheetTriangleBuffer(_samples_to_generate, Sheet1LeftVolts, Sheet1RightVolts, Sheet2LeftVolts, Sheet2RightVolts);
                _sheetWriter.WriteMultiSample(false, sheetBuffer);
                zBuffer = GenerateStaticZBuffer(_samples_to_generate);
                _zWriter.WriteMultiSample(false, zBuffer);
            }
        }

        private void ScanSheetLoop(CancellationToken token, SheetParams sheetParams)
        {
            double[,] sheetBuffer;
            while (_isRunning && !token.IsCancellationRequested)
            {
                try { WaitForAOSpace(_aoTask_sheet.Stream, token); }
                catch (OperationCanceledException) { break; }

                sheetBuffer = GenerateSheetTriangleBuffer(_samples_to_generate, sheetParams.Sheet1LeftVolts, sheetParams.Sheet1RightVolts, sheetParams.Sheet2LeftVolts, sheetParams.Sheet2RightVolts);
                _sheetWriter.WriteMultiSample(false, sheetBuffer);
            }

        }

        private void ScanZLoop(CancellationToken token, VolumeScanParams scanParams)
        {
            double[,] zBuffer;
            while (_isRunning && !token.IsCancellationRequested)
            {
                // To prevent buffer over-runs, which can lead to discontinuities, we wait until space for a full set of samples
                // is available in the bufffer, which we have configured to be twice the generated sample size.
                // Note: Our Z and sheet tasks are not synchronized. Therefore, for experimental loops we need to separate the threads and wait
                // on available samples independently to avoid jitter. However, for the idle loop we simply wait on the sheet and allow for jitter
                // on z in cases where the task is lagging behind in generation
                try { WaitForAOSpace(_aoTask_Z.Stream, token); }
                catch (OperationCanceledException) { break; }

                zBuffer = GenerateScanZBuffer(_samples_to_generate, scanParams);
                _zWriter.WriteMultiSample(false, zBuffer);
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Waits on buffer availability on a Daq stream to accomodate a full set of samples
        /// </summary>
        /// <param name="stream">The stream on which to wait</param>
        /// <param name="token">Global cancellation token</param>
        /// <exception cref="OperationCanceledException">Raises OperationCanceledException if cancellation was requested</exception>
        private void WaitForAOSpace(DaqStream stream, CancellationToken token) 
        {
            while(stream.OutputBufferSpaceAvailable < _samplesPerFrame)
            {
                if (token.IsCancellationRequested)
                    throw new OperationCanceledException();
            }
        }

        /// <summary>
        /// Loads waveform and buffer parameters from the application settings
        /// </summary>
        private void LoadWaveformSettings()
        {
            _samplesPerFrame = Settings.Default.SamplesPerFrame;
            _sweepsPerFrame = Settings.Default.SweepsPerFrame;
            _buffer_mult = Settings.Default.AOBufferMultiplier;
            _sheetTurnAround = Settings.Default.SheetTurnAround;
            _piezoSawtoothShape = Settings.Default.PiezoSawtoothShape;
        }

        private void NITaskSetup(int frameRateHz)
        {
            // On task generation we reset our global sample indices
            _sheet_sample_index = 0;
            _z_sample_index = 0;
            // TODO: When implementing actual z-scanning, we need to be cognizant of sample rates and buffer sizes to ensure that a) we do not need to regenerate buffers too quickly and b) that we do not exceed board buffer size
            // Implement non-automatic regeneration on all tasks regardless of the scan loop. Then to avoid both buffer over- and under-runs, we make the write buffer twice as large as the generated sample size, wait for enough space
            // to be available and then write another half-buffer to the board

            // Setup of analog tasks for mirror and piezo control
            _aoTask_sheet = new NationalInstruments.DAQmx.Task();
            _aoTask_sheet.AOChannels.CreateVoltageChannel(Sheet1Channel, "MirrorX1", Settings.Default.MirrorMinVolts, Settings.Default.MirrorMaxVolts, AOVoltageUnits.Volts);
            _aoTask_sheet.AOChannels.CreateVoltageChannel(Sheet2Channel, "MirrorX2", Settings.Default.MirrorMinVolts, Settings.Default.MirrorMaxVolts, AOVoltageUnits.Volts);
            // Make task generate continuous samples, do not allow automatic regeneration and manually set the buffer size
            _aoTask_sheet.Timing.ConfigureSampleClock("", _sampleRate, SampleClockActiveEdge.Rising, SampleQuantityMode.ContinuousSamples);
            _aoTask_sheet.Stream.WriteRegenerationMode = WriteRegenerationMode.DoNotAllowRegeneration;
            _aoTask_sheet.Stream.ConfigureOutputBuffer(_buffer_mult * _samples_to_generate);
            // TODO: Make determination whether there is a reason to synchronize these tasks to the frame and z-clock

            _sheetWriter = new AnalogMultiChannelWriter(_aoTask_sheet.Stream);

            _aoTask_Z = new NationalInstruments.DAQmx.Task();
            _aoTask_Z.AOChannels.CreateVoltageChannel(Z1Channel, "MirrorY1", Settings.Default.MirrorMinVolts, Settings.Default.MirrorMaxVolts, AOVoltageUnits.Volts);
            _aoTask_Z.AOChannels.CreateVoltageChannel(Z2Channel, "MirrorY2", Settings.Default.MirrorMinVolts, Settings.Default.MirrorMaxVolts, AOVoltageUnits.Volts);
            _aoTask_Z.AOChannels.CreateVoltageChannel(PiezoChannel, "Piezo", 0, Settings.Default.PiezoMaxVolts, AOVoltageUnits.Volts);
            _aoTask_Z.Timing.ConfigureSampleClock("", _sampleRate, SampleClockActiveEdge.Rising, SampleQuantityMode.ContinuousSamples);
            _aoTask_Z.Stream.WriteRegenerationMode = WriteRegenerationMode.DoNotAllowRegeneration;
            _aoTask_Z.Stream.ConfigureOutputBuffer(_buffer_mult * _samples_to_generate);
            _aoTask_Z.Triggers.StartTrigger.ConfigureDigitalEdgeTrigger(CounterInternalOutput, DigitalEdgeStartTriggerEdge.Rising);

            _zWriter = new AnalogMultiChannelWriter(_aoTask_Z.Stream);

            // Setup Counter Output Task for Camera Trigger
            _counterTask = new NationalInstruments.DAQmx.Task();
            _counterTask.COChannels.CreatePulseChannelFrequency(CounterChannel, "CameraTrigger", COPulseFrequencyUnits.Hertz, COPulseIdleState.Low, 0.0, frameRateHz, 0.5);
            _counterTask.ExportSignals.ExportHardwareSignal(ExportSignal.CounterOutputEvent, CounterOutputTerminal);
            _counterTask.Timing.ConfigureImplicit(SampleQuantityMode.ContinuousSamples);
        }

        /// <summary>
        /// Starts the scan system in idle/direct user control mode, i.e. no automated z-scanning will be performed
        /// </summary>
        /// <param name="frameRateHz">The desired camera framerate in Hz</param>
        /// <exception cref="InvalidOperationException">Raises InvalidOperationException if called while scan is running</exception>
        public void StartIdleScan(int frameRateHz)
        {
            if (_isRunning)
                throw new InvalidOperationException("Invoked StartIdleScan while ScanControl is running");
            LoadWaveformSettings();
            _sampleRate = frameRateHz * _samplesPerFrame;
            // For idle scan, to react appropriately fast to user input, we generate new samples every 50 ms
            _samples_to_generate = (int)(_sampleRate / 20);
            _isRunning = true;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            NITaskSetup(frameRateHz);
            double[,] sheetBuffer = GenerateSheetTriangleBuffer(_samples_to_generate * _buffer_mult, Sheet1LeftVolts, Sheet1RightVolts, Sheet2LeftVolts, Sheet2RightVolts);
            double[,] z_fixed_buffer = GenerateStaticZBuffer(_samples_to_generate * _buffer_mult);
            _sheetWriter.WriteMultiSample(false, sheetBuffer);
            _zWriter.WriteMultiSample(false, z_fixed_buffer);
            // Start AO tasks - they will wait on the counter for triggering
            _aoTask_sheet.Start();
            _aoTask_Z.Start();
            // Start the ao task loop
            System.Threading.Tasks.Task.Run(() => IdleLoop(_cancellationTokenSource.Token));
            // Pull trigger on everything by starting counter (NOTE: This includes the camera)
            _counterTask.Start();
        }

        public void StartZScan(VolumeScanParams scanParams)
        {
            if (_isRunning)
                throw new InvalidOperationException("Invoked StartZScan while ScanControl is running");
            LoadWaveformSettings();
            _sampleRate = scanParams.CameraFrameRate * _samplesPerFrame;
            // Fix our sheet extent to current settings as it is constant during volume scans
            var sheetParams = new SheetParams(Sheet1LeftVolts, Sheet1RightVolts, Sheet2LeftVolts, Sheet2RightVolts);
            // Calculate the number of samples in each volume sweep
            _z_period_samples = scanParams.FramesPerVolume * _samplesPerFrame;
            // For z-scan there is no direct user control. Generate 250 ms worth of samples ahead of time
            _samples_to_generate = (int)(_sampleRate / 4);
            _isRunning = true;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            NITaskSetup(scanParams.CameraFrameRate);
            double[,] sheetBuffer = GenerateSheetTriangleBuffer(_samples_to_generate * _buffer_mult, sheetParams.Sheet1LeftVolts, sheetParams.Sheet1RightVolts, sheetParams.Sheet2LeftVolts, sheetParams.Sheet2RightVolts);
            double[,] z_sweep_buffer = GenerateScanZBuffer(_samples_to_generate * _buffer_mult, scanParams);
            _sheetWriter.WriteMultiSample(false, sheetBuffer);
            _zWriter.WriteMultiSample(false, z_sweep_buffer);
            // Start AO tasks - they will wait on the counter for triggering
            _aoTask_sheet.Start();
            _aoTask_Z.Start();
            // Start the ao task loops
            System.Threading.Tasks.Task.Run(() => ScanSheetLoop(_cancellationTokenSource.Token, sheetParams));
            System.Threading.Tasks.Task.Run(() => ScanZLoop(_cancellationTokenSource.Token, scanParams));
            // Pull trigger on everything by starting counter (NOTE: This includes the camera)
            _counterTask.Start();
        }
        
        /// <summary>
        /// If no scan is running, sets all analog outputs to 0
        /// </summary>
        public void SetAllAOZero()
        {
            if (_isRunning)
                throw new InvalidOperationException("Attempted SetAllZero while ScanControl is running");
            var aoTask_sheet = new NationalInstruments.DAQmx.Task();
            aoTask_sheet.AOChannels.CreateVoltageChannel(Sheet1Channel, "MirrorX1", Settings.Default.MirrorMinVolts, Settings.Default.MirrorMaxVolts, AOVoltageUnits.Volts);
            aoTask_sheet.AOChannels.CreateVoltageChannel(Sheet2Channel, "MirrorX2", Settings.Default.MirrorMinVolts, Settings.Default.MirrorMaxVolts, AOVoltageUnits.Volts);

            var sheetWriter = new AnalogMultiChannelWriter(aoTask_sheet.Stream);
            sheetWriter.WriteSingleSample(true, [0, 0]);

            var aoTask_Z = new NationalInstruments.DAQmx.Task();
            aoTask_Z.AOChannels.CreateVoltageChannel(Z1Channel, "MirrorY1", Settings.Default.MirrorMinVolts, Settings.Default.MirrorMaxVolts, AOVoltageUnits.Volts);
            aoTask_Z.AOChannels.CreateVoltageChannel(Z2Channel, "MirrorY2", Settings.Default.MirrorMinVolts, Settings.Default.MirrorMaxVolts, AOVoltageUnits.Volts);
            aoTask_Z.AOChannels.CreateVoltageChannel(PiezoChannel, "Piezo", 0, Settings.Default.PiezoMaxVolts, AOVoltageUnits.Volts);

            var zWriter = new AnalogMultiChannelWriter(aoTask_Z.Stream);
            zWriter.WriteSingleSample(true, [0, 0, 0]);

            aoTask_sheet.Dispose();
            aoTask_Z.Dispose();
        }

        public void Stop()
        {
            if (!_isRunning)
                return;
            _cancellationTokenSource?.Cancel();
            _counterTask?.Stop();
            _aoTask_sheet?.Stop();
            _aoTask_Z?.Stop();
            _counterTask?.Dispose();
            _aoTask_sheet?.Dispose();
            _aoTask_Z?.Dispose();
            _counterTask = null;
            _aoTask_sheet = null;
            _aoTask_Z = null;
            _sheetWriter = null;
            _zWriter = null;
            _isRunning = false;
        }

        /// <summary>
        /// Generates two linked triangle buffers in which both triangle waves are synced and in phase but can have different extents
        /// </summary>
        /// <param name="totalSamples">The total number of samples to generate</param>
        /// <param name="cycles">The number of cycles to generate within total samples</param>
        /// <param name="vMin1">The lowest value for the first triangle wave</param>
        /// <param name="vMax1">The highest value for the first triangle wave</param>
        /// <param name="vMin2">The lowest value for the second triangle wave</param>
        /// <param name="vMax2">The highest value for the second triangle wave</param>
        /// <returns>The analog out buffer</returns>
        private double[,] GenerateSheetTriangleBuffer(int totalSamples, double sheet1left, double sheet1right, double sheet2left, double sheet2right)
        {
            // TODO: To make this work for both idle and z-scan, the sheet voltages should be parameters as they need to be fixed for experimental z-scanning!
            double[,] buffer = new double[2, totalSamples];
            int samplesPerCycle = _samplesPerFrame / _sweepsPerFrame;
            double voltage;

            for (int i = 0; i < totalSamples; i++)
            {
                long cycleSample = _sheet_sample_index % samplesPerCycle;
                double phase = (double)cycleSample / samplesPerCycle;

                voltage = sheet1left + ScanWaveforms.SmoothTriangle(phase, _sheetTurnAround) * (sheet1right - sheet1left);
                buffer[0, i] = voltage;

                voltage = sheet2left + ScanWaveforms.SmoothTriangle(phase, _sheetTurnAround) * (sheet2right - sheet2left);
                buffer[1, i] = voltage;
                _sheet_sample_index++;
            }
            return buffer;
        }

        /// <summary>
        /// Generates a static position z buffer jointly controlling both scan-heads and the piezo
        /// </summary>
        /// <param name="totalSamples">The total number of samples to generate</param>
        /// <returns>The analog out buffer</returns>
        private double[,] GenerateStaticZBuffer(int totalSamples)
        {
            double[,] buffer = new double[3, totalSamples];
            for (int i = 0; i < totalSamples; i++)
            {
                buffer[0, i] = Z1_Fixed;
                buffer[1, i] = Z2_Fixed;
                buffer[2, i] = _piezo_fixed_volts; // Not using property since we need it in volts
                _z_sample_index++;  // Not used here
            }
            return buffer;
        }

        private double[,] GenerateScanZBuffer(int totalSamples, VolumeScanParams scanparams)
        {

            double piezoMaxVolts = Settings.Default.PiezoMaxVolts;
            double piezoRangeMicrons = Settings.Default.PiezoRangeMicrons;

            double[,] buffer = new double[3, totalSamples];
            double voltage;
            double piezo_um;

            for (int i = 0; i < totalSamples; i++)
            {
                long cycleSample = _z_sample_index % _z_period_samples;
                double phase = (double)cycleSample / _z_period_samples;

                piezo_um = scanparams.StartMicrons + ScanWaveforms.Sawtooth(phase, _piezoSawtoothShape)*scanparams.DepthMicrons;
                voltage = piezo_um / piezoRangeMicrons * piezoMaxVolts;
                if (voltage > piezoMaxVolts)
                    voltage = piezoMaxVolts;
                buffer[2, i] = voltage;  // Piezo is the last channel
                buffer[0, i] = scanparams.ZConverterSheet1.ConvertPiezoToZ(piezo_um);
                buffer[1, i] = scanparams.ZConverterSheet2.ConvertPiezoToZ(piezo_um);
                _z_sample_index++;
            }

            return buffer;
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
                    _counterTask?.Dispose();
                    _aoTask_sheet?.Dispose();
                    _aoTask_Z?.Dispose();
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
