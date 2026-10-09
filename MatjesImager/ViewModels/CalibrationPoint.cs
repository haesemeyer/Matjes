using MatjesUtils;
using System;
using System.Collections.Generic;
using System.Text;

namespace MatjesImager.ViewModels
{
    /// <summary>
    /// One slot of the z-calibration: a piezo position with the in-focus z-mirror voltages
    /// of both sheets and a snapshot of the camera image at the time the point was set
    /// </summary>
    public class CalibrationPoint : PropertyChangeNotification, IDisposable
    {
        private bool _isSet;

        private double _piezoMicrons;

        private double _z1Volts;

        private double _z2Volts;

        private EZImageSource? _snapshot;

        private double _residual1Microns = double.NaN;

        private double _residual2Microns = double.NaN;

        private bool _residualWarning;

        public CalibrationPoint(int index)
        {
            Index = index;
        }

        /// <summary>
        /// The 1-based slot number
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Indicates whether this slot holds a calibration point
        /// </summary>
        public bool IsSet
        {
            get { return _isSet; }
            private set { _isSet = value; RaisePropertyChanged(nameof(IsSet)); }
        }

        /// <summary>
        /// The piezo position in microns
        /// </summary>
        public double PiezoMicrons
        {
            get { return _piezoMicrons; }
            private set { _piezoMicrons = value; RaisePropertyChanged(nameof(PiezoMicrons)); }
        }

        /// <summary>
        /// The in-focus z-mirror voltage of sheet 1
        /// </summary>
        public double Z1Volts
        {
            get { return _z1Volts; }
            private set { _z1Volts = value; RaisePropertyChanged(nameof(Z1Volts)); }
        }

        /// <summary>
        /// The in-focus z-mirror voltage of sheet 2
        /// </summary>
        public double Z2Volts
        {
            get { return _z2Volts; }
            private set { _z2Volts = value; RaisePropertyChanged(nameof(Z2Volts)); }
        }

        /// <summary>
        /// Camera image at the time the point was set
        /// </summary>
        public EZImageSource? Snapshot
        {
            get { return _snapshot; }
            private set { _snapshot = value; RaisePropertyChanged(nameof(Snapshot)); }
        }

        /// <summary>
        /// Deviation of sheet 1 from its fit line, expressed as the equivalent focal shift in microns
        /// </summary>
        public double Residual1Microns
        {
            get { return _residual1Microns; }
            set { _residual1Microns = value; RaisePropertyChanged(nameof(Residual1Microns)); }
        }

        /// <summary>
        /// Deviation of sheet 2 from its fit line, expressed as the equivalent focal shift in microns
        /// </summary>
        public double Residual2Microns
        {
            get { return _residual2Microns; }
            set { _residual2Microns = value; RaisePropertyChanged(nameof(Residual2Microns)); }
        }

        /// <summary>
        /// Indicates that at least one residual exceeds the warning threshold
        /// </summary>
        public bool ResidualWarning
        {
            get { return _residualWarning; }
            set { _residualWarning = value; RaisePropertyChanged(nameof(ResidualWarning)); }
        }

        /// <summary>
        /// Stores a calibration point in this slot, replacing any previous one
        /// </summary>
        /// <param name="piezoMicrons">The piezo position in microns</param>
        /// <param name="z1Volts">The in-focus voltage of sheet 1</param>
        /// <param name="z2Volts">The in-focus voltage of sheet 2</param>
        /// <param name="snapshot">The camera image to show for this point. Ownership passes to this object</param>
        public void Set(double piezoMicrons, double z1Volts, double z2Volts, EZImageSource? snapshot)
        {
            Snapshot?.Dispose();
            PiezoMicrons = piezoMicrons;
            Z1Volts = z1Volts;
            Z2Volts = z2Volts;
            Snapshot = snapshot;
            IsSet = true;
        }

        /// <summary>
        /// Removes the calibration point from this slot
        /// </summary>
        public void Clear()
        {
            Snapshot?.Dispose();
            Snapshot = null;
            IsSet = false;
            PiezoMicrons = 0;
            Z1Volts = 0;
            Z2Volts = 0;
            Residual1Microns = double.NaN;
            Residual2Microns = double.NaN;
            ResidualWarning = false;
        }

        public void Dispose()
        {
            Snapshot?.Dispose();
            Snapshot = null;
        }
    }
}
