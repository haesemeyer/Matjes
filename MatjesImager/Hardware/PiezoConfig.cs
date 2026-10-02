using System;
using System.Collections.Generic;
using System.Text;

namespace MatjesImager.Hardware
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using Thorlabs.MotionControl.DeviceManagerCLI;
    using Thorlabs.MotionControl.GenericPiezoCLI;
    using Thorlabs.MotionControl.GenericPiezoCLI.Piezo;
    using Thorlabs.MotionControl.Benchtop.PrecisionPiezoCLI;
    using Thorlabs.MotionControl.GenericPiezoCLI.Settings;

    public static class PiezoConfig
    {
        /// <summary>
        /// Configures single-channel PPC001 benchtop controller to first zero the controller,
        /// then activate closed loop and enable external BNC control and corrected position reporting
        /// </summary>
        /// <param name="serialNo">The serial number of the controller</param>
        /// <param name="zeroTimeoutMs">The timeout for zeroing the controller</param>
        public static void ConfigurePPC001(string serialNo, int zeroTimeoutMs = 30000)
        {

            DeviceManagerCLI.BuildDeviceList();

            BenchtopPrecisionPiezo device = BenchtopPrecisionPiezo.CreateBenchtopPiezo(serialNo);
            device.Connect(serialNo);

            PrecisionPiezoChannel channel = device.GetChannel(1);

            try
            {               

                if (!channel.IsSettingsInitialized())
                    channel.WaitForSettingsInitialized(5000);

                channel.StartPolling(250);
                Thread.Sleep(500);
                channel.EnableDevice();
                Thread.Sleep(500);

                // Zero in open loop, before enabling closed loop
                channel.SetPositionControlMode(PiezoControlModeTypes.OpenLoop);
                ZeroAndWait(channel, zeroTimeoutMs);
                // Closed loop and set to 0 before activating external BNC control
                channel.SetPositionControlMode(PiezoControlModeTypes.CloseLoop);
                var io_params = channel.GetIOParams();
                io_params.ControlSrc = IOSettings.PPCIOControlModes.SoftwareOnly;
                channel.SetIOParams(io_params);
                channel.SetPosition(0);

                // Control mode and output (monitor) mode
                io_params = channel.GetIOParams();
                io_params.ControlSrc = IOSettings.PPCIOControlModes.ExternalBNC;
                io_params.MonitorOPSig = IOSettings.PPCIOOutputModes.PositionCorrected;
                channel.SetIOParams(io_params);
            }
            finally
            {
                channel.StopPolling();
                device.Disconnect(true);
            }
        }

        /// <summary>
        /// Performs zeroing on the piezo controller
        /// </summary>
        /// <param name="channel">The device channel controlling the piezo</param>
        /// <param name="timeoutMs">Timeout for zeroing</param>
        /// <exception cref="TimeoutException"></exception>
        private static void ZeroAndWait(PrecisionPiezoChannel channel, int timeoutMs)
        {
            channel.SetZero();

            // Give the controller a moment to set the "zeroing" status flag
            Thread.Sleep(500);

            var sw = Stopwatch.StartNew();
            while (channel.Status.IsZeroing)
            {
                if (sw.ElapsedMilliseconds > timeoutMs)
                    throw new TimeoutException($"Zeroing did not complete within {timeoutMs} ms.");
                Thread.Sleep(100);
            }

        }
    }
}
