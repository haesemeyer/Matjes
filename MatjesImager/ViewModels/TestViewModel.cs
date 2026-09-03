using Hamamatsu.Dcam;
using Hamamatsu.Native;
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
            set { _piezo_fixed = value; RaisePropertyChanged(nameof(Piezo_Fixed)); RaisePropertyChanged(nameof(Pieze_Microns)); }
        }

        public string Pieze_Microns
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

        Image8? _camImage;

        private DcamCamera? _camera; // Using our custom P/Invoke wrapper
        private bool _isAcquiring = false;
        private CancellationTokenSource? _cancellationTokenSource;

        // NI-DAQmx Tasks
        private NationalInstruments.DAQmx.Task? _counterTask;
        private NationalInstruments.DAQmx.Task? _aoTask_sheet;
        private NationalInstruments.DAQmx.Task? _aoTask_Z;

        // Analog control channels
        private string _counterChannel = "Dev1/ctr0";
        private string _sheet1Channel = "Dev1/ao0";
        private string _sheet2Channel = "Dev1/ao2";

        private string _z1Channel = "Dev2/ao1";

        private string _z2Channel = "Dev2/ao2";

        private string _piezoChannel = "Dev2/ao0";

        private int _samplesPerFrame = 1000;
        private int _sweepsPerFrame = 2;

        public TestViewModel() {
            Sheet1LeftVolts = -1;
            Sheet1RightVolts = 1;
            Sheet2LeftVolts = -0.5;
            Sheet2RightVolts = 0.5;
            Z1_Fixed = 0;
            Z2_Fixed = 0;
            Piezo_Fixed = 0;
            if (IsInDesignMode)
                return;
            _camDisplay = new EZImageSource();
            StartAcquisition(100);
        }

        public void StartAcquisition(double frameRateHz)
        {
            try
            {
                // 1. Initialize Camera using custom wrapper
                _camera = new DcamCamera(0);

                _camera.SetPixelType(DcamNative.DCAM_PIXELTYPE.DCAM_PIXELTYPE_MONO8);
                _camera.SetReadoutSpeed(DcamNative.DCAM_READOUT_SPEED.DCAMPROP_READOUT_SPEED_FAST);
                _camera.SetROI(xOffset: 0, yOffset: 160, width: 2304, height: 2048);

                // 2. Configure Camera hardware trigger
                double exposureTime = (1.0 / frameRateHz);
                exposureTime -= exposureTime * 0.1;  // shorten 10% to give time for readout; might need to adjust this fraction
                
                //TODO: Change back to re-activate hardware trigger!
                //_camera.ConfigureHardwareTrigger(exposureTime);
                _camera.ConfigureInternalTrigger(exposureTime);

                // 3. Setup Analog Output Task for Mirrors
                _aoTask_sheet = new NationalInstruments.DAQmx.Task();
                _aoTask_sheet.AOChannels.CreateVoltageChannel(_sheet1Channel, "MirrorX1", -5, 5, AOVoltageUnits.Volts);
                _aoTask_sheet.AOChannels.CreateVoltageChannel(_sheet2Channel, "MirrorX2", -5, 5, AOVoltageUnits.Volts);

                _aoTask_Z = new NationalInstruments.DAQmx.Task();
                _aoTask_Z.AOChannels.CreateVoltageChannel(_z1Channel, "MirrorY1", -5, 5, AOVoltageUnits.Volts);
                _aoTask_Z.AOChannels.CreateVoltageChannel(_z2Channel, "MirrorY2", -5, 5, AOVoltageUnits.Volts);
                _aoTask_Z.AOChannels.CreateVoltageChannel(_piezoChannel, "Piezo", 0, 10, AOVoltageUnits.Volts);


                double aoSampleRate = frameRateHz * _samplesPerFrame;

                double[,] waveformBuffer = GenerateTriangleBuffer(_samplesPerFrame, _sweepsPerFrame, Sheet1LeftVolts, Sheet1RightVolts, Sheet2LeftVolts, Sheet2RightVolts);
                _aoTask_sheet.Timing.ConfigureSampleClock("", aoSampleRate, SampleClockActiveEdge.Rising, SampleQuantityMode.ContinuousSamples, _samplesPerFrame);
                _aoTask_sheet.Triggers.StartTrigger.ConfigureDigitalEdgeTrigger($"/Dev1/ctr0InternalOutput", DigitalEdgeStartTriggerEdge.Rising);

                double[,] z_fixed_buffer = GenerateZBuffer(_samplesPerFrame);
                _aoTask_Z.Timing.ConfigureSampleClock("", aoSampleRate, SampleClockActiveEdge.Rising, SampleQuantityMode.ContinuousSamples, _samplesPerFrame);
                //_aoTask_Z.Triggers.StartTrigger.ConfigureDigitalEdgeTrigger($"/Dev1/ctr0InternalOutput", DigitalEdgeStartTriggerEdge.Rising);

                AnalogMultiChannelWriter sheetWriter = new AnalogMultiChannelWriter(_aoTask_sheet.Stream);
                sheetWriter.WriteMultiSample(false, waveformBuffer);

                AnalogMultiChannelWriter zWriter = new AnalogMultiChannelWriter(_aoTask_Z.Stream);
                zWriter.WriteMultiSample(false, z_fixed_buffer);

                // 4. Setup Counter Output Task for Camera Trigger
                _counterTask = new NationalInstruments.DAQmx.Task();
                _counterTask.COChannels.CreatePulseChannelFrequency(_counterChannel, "CameraTrigger", COPulseFrequencyUnits.Hertz, COPulseIdleState.Low, 0.0, frameRateHz, 0.5);
                _counterTask.Timing.ConfigureImplicit(SampleQuantityMode.ContinuousSamples);

                // 5. Allocate Buffers and Arm Camera
                _camera.AllocateBuffer(10);
                _camera.StartCapture(DcamNative.DCAMCAP_START.SEQUENCE);

                _isAcquiring = true;
                _cancellationTokenSource = new CancellationTokenSource();

                // 6. Start AO task (enters armed state waiting for ctr0 pulse)
                _aoTask_sheet.Start();
                _aoTask_Z.Start();

                // 7. Launch image receiving thread
                System.Threading.Tasks.Task.Run(() => AcquisitionLoop(_cancellationTokenSource.Token));

                System.Threading.Tasks.Task.Run(() => SheetLoop(_cancellationTokenSource.Token));

                // 8. Start Counter Task LAST (fires everything synchronously)
                _counterTask.Start();
            }
            catch (Exception)
            {
                Cleanup();
                throw;
            }
        }

        private double[,] GenerateTriangleBuffer(int totalSamples, int cycles, double vMin1, double vMax1, double vMin2, double vMax2)
        {
            double[,] buffer = new double[2, totalSamples];
            int samplesPerCycle = totalSamples / cycles;
            double voltage;

            for (int i = 0; i < totalSamples; i++)
            {
                int cycleSample = i % samplesPerCycle;
                double phase = (double)cycleSample / samplesPerCycle;

                voltage = phase < 0.5
                    ? vMin1 + (vMax1 - vMin1) * (phase * 2.0)
                    : vMax1 - (vMax1 - vMin1) * ((phase - 0.5) * 2.0);
                buffer[0, i] = voltage;
                voltage = phase < 0.5
                    ? vMin2 + (vMax2 - vMin2) * (phase * 2.0)
                    : vMax2 - (vMax2 - vMin2) * ((phase - 0.5) * 2.0);
                buffer[1, i] = voltage;
            }
            return buffer;
        }

        private double[,] GenerateZBuffer(int totalSamples)
        {
            double[,] buffer = new double[3, totalSamples];
            for (int i = 0; i < totalSamples; i++)
            {
                buffer[0, i] = Z1_Fixed;
                buffer[1, i] = Z2_Fixed;
                buffer[2, i] = Piezo_Fixed;
            }
            return buffer;
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
            if (_camImage == null)
            {
                _camImage = new Image8(new ipp.IppiSize(width, height));
            }
            ipp.ip.ippiCopy_8u_C1R((byte*)unmanagedBuffer, rowBytes, _camImage.Image, _camImage.Stride, _camImage.Size);
            if (FrameIndex % 10 == 0)
            {
                try
                {
                    CamDisplay.Write(_camImage, token.WaitHandle);
                }
                catch (OperationCanceledException) { }
            }
        }

        private void SheetLoop(CancellationToken token)
        {
            double[,] waveformBuffer;
            double[,] zBuffer;
            AnalogMultiChannelWriter sheetWriter = new AnalogMultiChannelWriter(_aoTask_sheet.Stream);
            AnalogMultiChannelWriter zWriter = new AnalogMultiChannelWriter(_aoTask_Z.Stream);
            while (_isAcquiring && !token.IsCancellationRequested)
            {
                waveformBuffer = GenerateTriangleBuffer(_samplesPerFrame, _sweepsPerFrame, Sheet1LeftVolts, Sheet1RightVolts, Sheet2LeftVolts, Sheet2RightVolts);
                sheetWriter.WriteMultiSample(false, waveformBuffer);
                zBuffer = GenerateZBuffer(_samplesPerFrame);
                zWriter.WriteMultiSample(false, zBuffer);
                Thread.Sleep(100);
            }
        }

        public void StopAcquisition()
        {
            _isAcquiring = false;
            _cancellationTokenSource?.Cancel();

            _counterTask?.Stop();
            _aoTask_sheet?.Stop();

            if (_camera != null)
            {
                _camera.StopCapture();
                _camera.ReleaseBuffer();
            }
        }

        private void Cleanup()
        {
            _counterTask?.Dispose();
            _aoTask_sheet?.Dispose();

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
