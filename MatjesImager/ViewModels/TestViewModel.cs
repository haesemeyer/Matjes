using Hamamatsu.Dcam;
using Hamamatsu.Native;
using MatjesUtils;
using NationalInstruments.DAQmx;
using System;
using System.Collections.Generic;
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

        Image8? _camImage;

        private DcamCamera? _camera; // Using our custom P/Invoke wrapper
        private bool _isAcquiring = false;
        private CancellationTokenSource? _cancellationTokenSource;

        // NI-DAQmx Tasks
        private NationalInstruments.DAQmx.Task? _counterTask;
        private NationalInstruments.DAQmx.Task? _aoTask;

        private string _counterChannel = "Dev1/ctr0";
        private string _aoChannel0 = "Dev1/ao0";
        private string _aoChannel1 = "Dev1/ao1";

        public TestViewModel() {
            if (IsInDesignMode)
                return;
            _camDisplay = new EZImageSource();
            StartAcquisition(10, -1, 1);
        }

        public void StartAcquisition(double frameRateHz, double minVolts, double maxVolts, int sweepsPerFrame = 10)
        {
            try
            {
                // 1. Initialize Camera using custom wrapper
                _camera = new DcamCamera(0);

                _camera.SetPixelType(DcamNative.DCAM_PIXELTYPE.DCAM_PIXELTYPE_MONO8);

                // 2. Configure Camera hardware trigger
                double exposureTime = (1.0 / frameRateHz) - 0.002;
                
                //TODO: Change back to re-activate hardware trigger!
                //_camera.ConfigureHardwareTrigger(exposureTime);
                _camera.ConfigureInternalTrigger(exposureTime);

                // 3. Setup Analog Output Task for Mirrors
                _aoTask = new NationalInstruments.DAQmx.Task();
                _aoTask.AOChannels.CreateVoltageChannel(_aoChannel0, "Mirror1", minVolts, maxVolts, AOVoltageUnits.Volts);
                _aoTask.AOChannels.CreateVoltageChannel(_aoChannel1, "Mirror2", minVolts, maxVolts, AOVoltageUnits.Volts);

                int samplesPerFrame = 1000;
                double aoSampleRate = frameRateHz * samplesPerFrame;
                double[,] waveformBuffer = GenerateTriangleBuffer(samplesPerFrame, sweepsPerFrame, minVolts, maxVolts);

                _aoTask.Timing.ConfigureSampleClock("", aoSampleRate, SampleClockActiveEdge.Rising, SampleQuantityMode.ContinuousSamples, samplesPerFrame);
                _aoTask.Triggers.StartTrigger.ConfigureDigitalEdgeTrigger($"/Dev1/ctr0InternalOutput", DigitalEdgeStartTriggerEdge.Rising);

                AnalogMultiChannelWriter aoWriter = new AnalogMultiChannelWriter(_aoTask.Stream);
                aoWriter.WriteMultiSample(false, waveformBuffer);

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
                _aoTask.Start();

                // 7. Launch image receiving thread
                System.Threading.Tasks.Task.Run(() => AcquisitionLoop(_cancellationTokenSource.Token));

                // 8. Start Counter Task LAST (fires everything synchronously)
                _counterTask.Start();

                Console.WriteLine($"Custom wrapper acquisition started at {frameRateHz} Hz.");
            }
            catch (Exception)
            {
                Cleanup();
                throw;
            }
        }

        private double[,] GenerateTriangleBuffer(int totalSamples, int cycles, double vMin, double vMax)
        {
            double[,] buffer = new double[2, totalSamples];
            int samplesPerCycle = totalSamples / cycles;

            for (int i = 0; i < totalSamples; i++)
            {
                int cycleSample = i % samplesPerCycle;
                double phase = (double)cycleSample / samplesPerCycle;

                double voltage = phase < 0.5
                    ? vMin + (vMax - vMin) * (phase * 2.0)
                    : vMax - (vMax - vMin) * ((phase - 0.5) * 2.0);

                buffer[0, i] = voltage;
                buffer[1, i] = voltage;
            }
            return buffer;
        }

        private void AcquisitionLoop(CancellationToken token)
        {
            while (_isAcquiring && !token.IsCancellationRequested)
            {
                // Using the custom wait method
                if (_camera.WaitForFrame(2000))
                {
                    // Using the custom transfer methods
                    _camera.GetTransferInfo(out int frameCount, out int newestFrameIndex);

                    DcamNative.DCAMBUF_FRAME frame = _camera.LockFrame(newestFrameIndex);

                    ProcessFrame(frame.buf, frame.width, frame.height, frame.rowbytes, token);
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
            //ipp.ip.ippiSet_8u_C1R(0, _camImage.Image, _camImage.Stride, _camImage.Size);
            try
            {
                CamDisplay.Write(_camImage, token.WaitHandle);
            }
            catch (OperationCanceledException) { }
        }

        public void StopAcquisition()
        {
            _isAcquiring = false;
            _cancellationTokenSource?.Cancel();

            _counterTask?.Stop();
            _aoTask?.Stop();

            if (_camera != null)
            {
                _camera.StopCapture();
                _camera.ReleaseBuffer();
            }
        }

        private void Cleanup()
        {
            _counterTask?.Dispose();
            _aoTask?.Dispose();

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

        /*public void StartConfiguredAcquisition()
        {
            _camera = new DcamCamera(0);

            // 1. Setup Camera Geometry & Bit Depth
            _camera.SetBitDepth(16);          // 16-bit output
            _camera.SetBinning(2);            // 2x2 Binning (cuts resolution in half, increases speed/SNR)

            // Set a 1024x1024 ROI centered on the binned sensor space
            _camera.SetROI(xOffset: 512, yOffset: 512, width: 1024, height: 1024);

            // 2. Setup Triggers (Using the previous hardware sync setup)
            _camera.ConfigureHardwareTrigger(0.008); // 8ms exposure

            // 3. Allocate buffers based on the new geometry sizes
            // (If you allocate before setting ROI/Binning, the buffers will be the wrong size and cap_start will fail)
            _camera.AllocateBuffer(10);

            // 4. Start Capture
            _camera.StartCapture(DcamNative.DCAMCAP_START.SEQUENCE);

            // ... begin waiting/trigger loop ...
        }*/
    }
    }
