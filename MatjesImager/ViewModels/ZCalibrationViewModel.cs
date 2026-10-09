using MatjesImager.Hardware;
using MatjesUtils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;

namespace MatjesImager.ViewModels
{
    /// <summary>
    /// View model for calibrating the relationship between objective piezo position (in microns)
    /// and the z-mirror command voltage of each of the two light sheets.
    /// The user sets a piezo position, focuses both sheets and stores the result as a calibration point.
    /// For each sheet the relationship V = slope * um + offset is fit by least squares across all stored points.
    /// Since the piezo position is set precisely and the sheet voltage is judged by eye, the fit minimizes the error in voltage.
    /// </summary>
    public class ZCalibrationViewModel : ViewModelBase
    {
        #region Constants

        /// <summary>
        /// The number of calibration slots
        /// </summary>
        public const int NumCalibrationPoints = 5;

        /// <summary>
        /// The minimum number of points required before a calibration can be accepted
        /// </summary>
        public const int MinimumPointsToAccept = 3;

        /// <summary>
        /// The maximum command voltage of the z-mirrors (matches the range of the z-mirror AO channels in ScanControl)
        /// </summary>
        public const double Z_V_Max = 5;

        /// <summary>
        /// The minimum command voltage of the z-mirrors (matches the range of the z-mirror AO channels in ScanControl)
        /// </summary>
        public const double Z_V_Min = -5;

        /// <summary>
        /// The travel range of the piezo in microns
        /// </summary>
        public const double PiezoMaxMicrons = 450;

        /// <summary>
        /// Two calibration points closer than this (in microns) are considered to be at the same piezo position
        /// </summary>
        private const double _minPiezoSeparation = 1;

        #endregion

        #region Members

        private ScanControl _scanhead;

        private CameraStream? _camera;

        private LinearFitResult? _fit1;

        private LinearFitResult? _fit2;

        private Point[] _sheet1PlotPoints = [];

        private Point[] _sheet2PlotPoints = [];

        private bool _followFit = true;

        private double _zStepVolts = 0.005;

        private double _residualWarningMicrons = 2.5;

        private string _statusMessage = "";

        private LinearZConverter? _sheet1Converter;

        private LinearZConverter? _sheet2Converter;

        /// <summary>
        /// Prevents FollowFit from fighting with GoToCalibrationPoint, which sets piezo and sheets jointly
        /// </summary>
        private bool _suppressFollow = false;

        #endregion

        public ZCalibrationViewModel()
        {
            CalibrationPoints = new ObservableCollection<CalibrationPoint>();
            for (int i = 0; i < NumCalibrationPoints; i++)
                CalibrationPoints.Add(new CalibrationPoint(i + 1));
            // The converters are irrelevant during calibration since only the idle scan with fixed positions is used
            _scanhead = new ScanControl(new LinearZConverter(-4.0 / 450, 2, Z_V_Max, Z_V_Min), new LinearZConverter(-4.0 / 450, 2, Z_V_Max, Z_V_Min));
            _scanhead.PropertyChanged += Scanhead_PropertyChanged;
            StatusMessage = "Move the piezo into the sample, focus both sheets and add a calibration point.";
            if (IsInDesignMode)
                return;
            _scanhead.SetAllAOZero();
            PiezoConfig.ConfigurePPC001("44506384");// Set Closed loop, external BNC control and corrected position report on Piezo
            _camera = new CameraStream();
            // Camera has to be armed before the scanhead starts the trigger counter
            _camera.Start();
            _scanhead.StartIdleScan(100);
        }

        #region Properties

        /// <summary>
        /// Control of mirrors and piezo. During calibration the idle scan is used, i.e. fixed piezo and sheet z-positions
        /// </summary>
        public ScanControl Scanhead => _scanhead;

        /// <summary>
        /// The live camera stream
        /// </summary>
        public CameraStream? Camera => _camera;

        /// <summary>
        /// The calibration slots
        /// </summary>
        public ObservableCollection<CalibrationPoint> CalibrationPoints { get; }

        /// <summary>
        /// The number of slots that currently hold a calibration point
        /// </summary>
        public int NumSetPoints => CalibrationPoints.Count(p => p.IsSet);

        /// <summary>
        /// Indicates whether there is a free slot for another calibration point
        /// </summary>
        public bool CanAddPoint => NumSetPoints < NumCalibrationPoints;

        /// <summary>
        /// Indicates whether the current fits can be accepted as calibration
        /// </summary>
        public bool CanAccept => Fit1 != null && Fit2 != null && NumSetPoints >= MinimumPointsToAccept;

        /// <summary>
        /// The current fit for sheet 1 or null if fewer than two distinct points are set
        /// </summary>
        public LinearFitResult? Fit1
        {
            get { return _fit1; }
            private set { _fit1 = value; RaisePropertyChanged(nameof(Fit1)); }
        }

        /// <summary>
        /// The current fit for sheet 2 or null if fewer than two distinct points are set
        /// </summary>
        public LinearFitResult? Fit2
        {
            get { return _fit2; }
            private set { _fit2 = value; RaisePropertyChanged(nameof(Fit2)); }
        }

        /// <summary>
        /// Calibration points of sheet 1 as (piezo um, volts) for plotting
        /// </summary>
        public Point[] Sheet1PlotPoints
        {
            get { return _sheet1PlotPoints; }
            private set { _sheet1PlotPoints = value; RaisePropertyChanged(nameof(Sheet1PlotPoints)); }
        }

        /// <summary>
        /// Calibration points of sheet 2 as (piezo um, volts) for plotting
        /// </summary>
        public Point[] Sheet2PlotPoints
        {
            get { return _sheet2PlotPoints; }
            private set { _sheet2PlotPoints = value; RaisePropertyChanged(nameof(Sheet2PlotPoints)); }
        }

        /// <summary>
        /// If true and a fit exists, moving the piezo moves both sheets to their predicted in-focus voltage
        /// so that only a fine adjustment is needed for subsequent points
        /// </summary>
        public bool FollowFit
        {
            get { return _followFit; }
            set { _followFit = value; RaisePropertyChanged(nameof(FollowFit)); }
        }

        /// <summary>
        /// The voltage step of the fine z-adjustment buttons
        /// </summary>
        public double ZStepVolts
        {
            get { return _zStepVolts; }
            set { _zStepVolts = Math.Abs(value); RaisePropertyChanged(nameof(ZStepVolts)); }
        }

        /// <summary>
        /// Calibration points whose residual (expressed in microns of focal shift) exceeds this value are flagged.
        /// Should be about a quarter of the sheet thickness
        /// </summary>
        public double ResidualWarningMicrons
        {
            get { return _residualWarningMicrons; }
            set { _residualWarningMicrons = Math.Abs(value); RaisePropertyChanged(nameof(ResidualWarningMicrons)); UpdateFits(); }
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
        /// The accepted calibration of sheet 1 (null until a calibration was accepted)
        /// </summary>
        public LinearZConverter? Sheet1Converter
        {
            get { return _sheet1Converter; }
            private set { _sheet1Converter = value; RaisePropertyChanged(nameof(Sheet1Converter)); }
        }

        /// <summary>
        /// The accepted calibration of sheet 2 (null until a calibration was accepted)
        /// </summary>
        public LinearZConverter? Sheet2Converter
        {
            get { return _sheet2Converter; }
            private set { _sheet2Converter = value; RaisePropertyChanged(nameof(Sheet2Converter)); }
        }

        /// <summary>
        /// Raised when the user accepts a calibration. Sheet1Converter and Sheet2Converter hold the result
        /// </summary>
        public event EventHandler? CalibrationAccepted;

        #endregion

        #region Methods

        /// <summary>
        /// Stores the current piezo position and sheet voltages together with a snapshot of the camera image in the first free slot
        /// </summary>
        public void AddCalibrationPoint()
        {
            var slot = CalibrationPoints.FirstOrDefault(p => !p.IsSet);
            if (slot == null)
            {
                StatusMessage = "All calibration slots are used. Clear a point to replace it.";
                return;
            }
            double piezo = Scanhead.Piezo_Fixed_Microns;
            if (CalibrationPoints.Any(p => p.IsSet && Math.Abs(p.PiezoMicrons - piezo) < _minPiezoSeparation))
            {
                StatusMessage = $"A calibration point at {piezo:F0} um already exists. Move the piezo to a different depth first.";
                return;
            }
            slot.Set(piezo, Scanhead.Z1_Fixed, Scanhead.Z2_Fixed, TakeSnapshot());
            UpdateFits();
            if (NumSetPoints < NumCalibrationPoints)
                StatusMessage = $"Added point {slot.Index} at {piezo:F0} um. Move the piezo to the next depth.";
            else
                StatusMessage = "All points set. Check the residuals and accept the calibration.";
        }

        /// <summary>
        /// Removes a calibration point
        /// </summary>
        public void RemoveCalibrationPoint(CalibrationPoint point)
        {
            point.Clear();
            UpdateFits();
            StatusMessage = $"Cleared point {point.Index}.";
        }

        /// <summary>
        /// Removes all calibration points
        /// </summary>
        public void ClearCalibrationPoints()
        {
            foreach (var p in CalibrationPoints)
                p.Clear();
            UpdateFits();
            StatusMessage = "Cleared all points.";
        }

        /// <summary>
        /// Moves piezo and both sheets back to the positions stored in a calibration point, e.g. to re-check focus
        /// </summary>
        public void GoToCalibrationPoint(CalibrationPoint point)
        {
            if (!point.IsSet)
                return;
            _suppressFollow = true;
            try
            {
                Scanhead.Piezo_Fixed_Microns = point.PiezoMicrons;
                Scanhead.Z1_Fixed = point.Z1Volts;
                Scanhead.Z2_Fixed = point.Z2Volts;
            }
            finally { _suppressFollow = false; }
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

        /// <summary>
        /// Generates the calibration objects from the current fits and raises CalibrationAccepted
        /// </summary>
        public void AcceptCalibration()
        {
            if (!CanAccept)
            {
                StatusMessage = $"At least {MinimumPointsToAccept} calibration points are required.";
                return;
            }
            Sheet1Converter = new LinearZConverter(Fit1!.Slope, Fit1.Offset, Z_V_Max, Z_V_Min);
            Sheet2Converter = new LinearZConverter(Fit2!.Slope, Fit2.Offset, Z_V_Max, Z_V_Min);
            StatusMessage = $"Calibration accepted. Sheet 1: R2 = {Fit1.RSquared:F4}, sheet 2: R2 = {Fit2.RSquared:F4}.";
            CalibrationAccepted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Copies the most recent camera frame into a new display source
        /// </summary>
        private EZImageSource? TakeSnapshot()
        {
            using var frame = _camera?.CopyLatestFrame();
            if (frame == null)
                return null;
            var snapshot = new EZImageSource_LH();
            // We are on the UI thread so the write completes synchronously and the handle is never signaled
            using var never = new System.Threading.ManualResetEvent(false);
            snapshot.Write(frame, never);
            return snapshot;
        }

        /// <summary>
        /// Refits both sheets to the set calibration points and updates residuals and plot data
        /// </summary>
        private void UpdateFits()
        {
            var set = CalibrationPoints.Where(p => p.IsSet).ToList();
            var piezo = set.Select(p => p.PiezoMicrons).ToArray();
            var z1 = set.Select(p => p.Z1Volts).ToArray();
            var z2 = set.Select(p => p.Z2Volts).ToArray();
            Sheet1PlotPoints = piezo.Zip(z1, (x, y) => new Point(x, y)).ToArray();
            Sheet2PlotPoints = piezo.Zip(z2, (x, y) => new Point(x, y)).ToArray();
            if (set.Count >= 2)
            {
                // AddCalibrationPoint ensures distinct piezo positions so the fit is defined
                Fit1 = LinearFit.Fit(piezo, z1);
                Fit2 = LinearFit.Fit(piezo, z2);
            }
            else
            {
                Fit1 = null;
                Fit2 = null;
            }
            foreach (var p in CalibrationPoints)
            {
                int i = set.IndexOf(p);
                if (i < 0 || Fit1 == null || Fit2 == null)
                {
                    p.Residual1Microns = double.NaN;
                    p.Residual2Microns = double.NaN;
                    p.ResidualWarning = false;
                    continue;
                }
                // Convert the voltage residual into the equivalent focal shift in microns
                p.Residual1Microns = Fit1.Residuals[i] / Fit1.Slope;
                p.Residual2Microns = Fit2.Residuals[i] / Fit2.Slope;
                p.ResidualWarning = Math.Abs(p.Residual1Microns) > ResidualWarningMicrons || Math.Abs(p.Residual2Microns) > ResidualWarningMicrons;
            }
            RaisePropertyChanged(nameof(NumSetPoints));
            RaisePropertyChanged(nameof(CanAddPoint));
            RaisePropertyChanged(nameof(CanAccept));
        }

        private void Scanhead_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ScanControl.Piezo_Fixed_Microns) || !FollowFit || _suppressFollow)
                return;
            if (Fit1 == null || Fit2 == null)
                return;
            double um = Scanhead.Piezo_Fixed_Microns;
            Scanhead.Z1_Fixed = Math.Clamp(Fit1.Predict(um), Z_V_Min, Z_V_Max);
            Scanhead.Z2_Fixed = Math.Clamp(Fit2.Predict(um), Z_V_Min, Z_V_Max);
        }

        #endregion

        #region IDisposable

        override protected void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                _scanhead.PropertyChanged -= Scanhead_PropertyChanged;
                if (!IsInDesignMode)
                {
                    _scanhead.Stop();
                    _camera?.Dispose();
                    _scanhead.SetAllAOZero();
                }
                _scanhead.Dispose();
                foreach (var p in CalibrationPoints)
                    p.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
