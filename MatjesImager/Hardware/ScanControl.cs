using MatjesUtils;
using NationalInstruments.DAQmx;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
        private const int _sweepsPerFrame = 4;

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

        // Analog control channel definitions before these move into properties
        private const string _counterChannel = "Dev2/ctr0";
        private const string _counterOutput_terminal = "/Dev2/PFI0";
        private const string _sheet1Channel = "Dev1/ao0";
        private const string _sheet2Channel = "Dev1/ao2";

        private const string _z1Channel = "Dev2/ao1";

        private const string _z2Channel = "Dev2/ao2";

        private const string _piezoChannel = "Dev2/ao0";

        /// <summary>
        /// The number of ao samples to generate for each camera frame
        /// </summary>
        private const int _samplesPerFrame = 1000;


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
            get { return _piezo_fixed_volts / 10.0 * 450.0; }
            set { _piezo_fixed_volts = value / 450.0 * 10.0; RaisePropertyChanged(nameof(Piezo_Fixed_Microns)); }
        }

        #endregion

        #region ThreadProcs

        private void IdleLoop(CancellationToken token)
        {
            double[,] sheetBuffer;
            double[,] zBuffer;
            while (_isRunning && !token.IsCancellationRequested)
            {
                sheetBuffer = GenerateSheetTriangleBuffer(_samplesPerFrame, _sweepsPerFrame);
                _sheetWriter.WriteMultiSample(false, sheetBuffer);
                zBuffer = GenerateStaticZBuffer(_samplesPerFrame);
                _zWriter.WriteMultiSample(false, zBuffer);
                Thread.Sleep(100);
            }
        }

        #endregion

        #region Methods

        private void NITaskSetup(int frameRateHz)
        {
            // TODO: When implementing actual z-scanning, we need to be cognizant of sample rates and buffer sizes to ensure that a) we do not need to regenerate buffers too quickly and b) that we do not exceed board buffer size

            // Setup of analog tasks for mirror and piezo control
            _aoTask_sheet = new NationalInstruments.DAQmx.Task();
            _aoTask_sheet.AOChannels.CreateVoltageChannel(_sheet1Channel, "MirrorX1", -5, 5, AOVoltageUnits.Volts);
            _aoTask_sheet.AOChannels.CreateVoltageChannel(_sheet2Channel, "MirrorX2", -5, 5, AOVoltageUnits.Volts);
            _aoTask_sheet.Timing.ConfigureSampleClock("", _sampleRate, SampleClockActiveEdge.Rising, SampleQuantityMode.ContinuousSamples, _samplesPerFrame);
            // TODO: Make determination whether there is a reason to synchronize these tasks to the frame and z-clock

            _sheetWriter = new AnalogMultiChannelWriter(_aoTask_sheet.Stream);

            _aoTask_Z = new NationalInstruments.DAQmx.Task();
            _aoTask_Z.AOChannels.CreateVoltageChannel(_z1Channel, "MirrorY1", -5, 5, AOVoltageUnits.Volts);
            _aoTask_Z.AOChannels.CreateVoltageChannel(_z2Channel, "MirrorY2", -5, 5, AOVoltageUnits.Volts);
            _aoTask_Z.AOChannels.CreateVoltageChannel(_piezoChannel, "Piezo", 0, 10, AOVoltageUnits.Volts);
            _aoTask_Z.Timing.ConfigureSampleClock("", _sampleRate, SampleClockActiveEdge.Rising, SampleQuantityMode.ContinuousSamples, _samplesPerFrame);
            _aoTask_Z.Triggers.StartTrigger.ConfigureDigitalEdgeTrigger($"/Dev2/ctr0InternalOutput", DigitalEdgeStartTriggerEdge.Rising);

            _zWriter = new AnalogMultiChannelWriter(_aoTask_Z.Stream);

            // Setup Counter Output Task for Camera Trigger
            _counterTask = new NationalInstruments.DAQmx.Task();
            _counterTask.COChannels.CreatePulseChannelFrequency(_counterChannel, "CameraTrigger", COPulseFrequencyUnits.Hertz, COPulseIdleState.Low, 0.0, frameRateHz, 0.5);
            _counterTask.ExportSignals.ExportHardwareSignal(ExportSignal.CounterOutputEvent, _counterOutput_terminal);
            _counterTask.Timing.ConfigureImplicit(SampleQuantityMode.ContinuousSamples);
        }

        /// <summary>
        /// Starts the scan system in idle/direct user control mode, i.e. no automated z-scanning will be performed
        /// </summary>
        /// <param name="frameRateHz">The desired camera framerate in Hz</param>
        public void StartIdleScan(int frameRateHz)
        {
            _sampleRate = frameRateHz * _samplesPerFrame;
            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            double aoSampleRate = frameRateHz * _samplesPerFrame;
            NITaskSetup(frameRateHz);
            // NOTE: Since we regenerate samples, we do not have to care about the size of the write buffer that requires more consideration for scanning
            double[,] sheetBuffer = GenerateSheetTriangleBuffer(_samplesPerFrame, _sweepsPerFrame);
            double[,] z_fixed_buffer = GenerateStaticZBuffer(_samplesPerFrame);
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
        private double[,] GenerateSheetTriangleBuffer(int totalSamples, int cycles)
        {
            double[,] buffer = new double[2, totalSamples];
            int samplesPerCycle = totalSamples / cycles;
            double voltage;

            for (int i = 0; i < totalSamples; i++)
            {
                int cycleSample = i % samplesPerCycle;
                double phase = (double)cycleSample / samplesPerCycle;

                voltage = Sheet1LeftVolts + ScanWaveforms.SmoothTriangle(phase, 0.1) * (Sheet1RightVolts - Sheet1LeftVolts);
                buffer[0, i] = voltage;

                voltage = Sheet2LeftVolts + ScanWaveforms.SmoothTriangle(phase, 0.1) * (Sheet2RightVolts - Sheet2LeftVolts);
                buffer[1, i] = voltage;
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
