using MatjesImager.Hardware;
using MatjesUtils;
using System;
using System.Collections.Generic;
using System.Text;

namespace MatjesImager.ViewModels
{
    /// <summary>
    /// The fixed piezo and sheet positions used in preview, shared by all steps that run in preview.
    /// The idle scan picks up changes to the scan head properties directly
    /// </summary>
    public class PositionsViewModel : ViewModelBase
    {
        private double _zStepVolts = 0.005;

        /// <summary>
        /// The maximum command voltage of the z-mirrors
        /// </summary>
        public static double Z_V_Max => Properties.Settings.Default.MirrorMaxVolts;

        /// <summary>
        /// The minimum command voltage of the z-mirrors
        /// </summary>
        public static double Z_V_Min => Properties.Settings.Default.MirrorMinVolts;

        /// <summary>
        /// Control of mirrors and piezo
        /// </summary>
        public ScanControl Scanhead => App.LightSheet.ScanHead;

        /// <summary>
        /// The voltage step of the fine z-adjustment buttons
        /// </summary>
        public double ZStepVolts
        {
            get { return _zStepVolts; }
            set { _zStepVolts = Math.Abs(value); RaisePropertyChanged(nameof(ZStepVolts)); }
        }

        /// <summary>
        /// Moves the z-position of sheet 1 by a number of fine steps
        /// </summary>
        public void NudgeZ1(int steps)
        {
            Scanhead.Z1_Fixed = Math.Clamp(Scanhead.Z1_Fixed + steps * ZStepVolts, Z_V_Min, Z_V_Max);
        }

        /// <summary>
        /// Moves the z-position of sheet 2 by a number of fine steps
        /// </summary>
        public void NudgeZ2(int steps)
        {
            Scanhead.Z2_Fixed = Math.Clamp(Scanhead.Z2_Fixed + steps * ZStepVolts, Z_V_Min, Z_V_Max);
        }
    }
}
