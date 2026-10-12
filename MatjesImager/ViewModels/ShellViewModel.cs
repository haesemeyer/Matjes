using MatjesImager.Hardware;
using MatjesUtils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace MatjesImager.ViewModels
{
    /// <summary>
    /// View model of the main window: the ordered workflow steps, the selected step and the shared microscope.
    /// Selecting a step switches the microscope into the mode the step requires
    /// </summary>
    public class ShellViewModel : ViewModelBase
    {
        private StepViewModel? _selectedStep;

        private string _statusMessage = "";

        public ShellViewModel()
        {
            Positions = new PositionsViewModel();
            Preview = new PreviewViewModel();
            ZCalibration = new ZCalibrationViewModel();
            Steps = new ObservableCollection<StepViewModel>
            {
                Preview,
                new PlaceholderStepViewModel(2, "ROI", MicroscopeMode.Preview,
                    "Selecting the camera region of interest is not built yet."),
                ZCalibration,
                new PlaceholderStepViewModel(4, "Volume scan", null,
                    "Setting up and testing the volume scan is not built yet."),
                new PlaceholderStepViewModel(5, "Experiment", null,
                    "Running experiments is not built yet."),
            };
            if (IsInDesignMode)
            {
                _selectedStep = ZCalibration;
                return;
            }
            SelectedStep = Steps[0];
        }

        #region Properties

        /// <summary>
        /// The shared microscope
        /// </summary>
        public Microscope LightSheet => App.LightSheet;

        /// <summary>
        /// The shared position controls shown for steps that run in preview
        /// </summary>
        public PositionsViewModel Positions { get; }

        /// <summary>
        /// The preview step, which also holds the preview settings shared by all steps running in preview
        /// </summary>
        public PreviewViewModel Preview { get; }

        /// <summary>
        /// The z-calibration step
        /// </summary>
        public ZCalibrationViewModel ZCalibration { get; }

        /// <summary>
        /// The workflow steps in order
        /// </summary>
        public ObservableCollection<StepViewModel> Steps { get; }

        /// <summary>
        /// The step whose panel is shown. Setting it switches the microscope into the mode the step requires
        /// </summary>
        public StepViewModel? SelectedStep
        {
            get { return _selectedStep; }
            set
            {
                if (value == _selectedStep)
                    return;
                _selectedStep?.Deactivate();
                _selectedStep = value;
                RaisePropertyChanged(nameof(SelectedStep));
                if (value == null)
                    return;
                if (EnterMode(value.RequiredMode))
                    value.Activate();
            }
        }

        /// <summary>
        /// Hardware errors and other messages that concern the whole application
        /// </summary>
        public string StatusMessage
        {
            get { return _statusMessage; }
            private set { _statusMessage = value; RaisePropertyChanged(nameof(StatusMessage)); }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Stops and restarts the microscope if it is not already in the requested mode.
        /// Steps 1 to 3 all run in preview so moving between them does not touch the hardware
        /// </summary>
        /// <param name="mode">The requested mode or null to leave the microscope as it is</param>
        /// <returns>True if the microscope is in the requested mode</returns>
        private bool EnterMode(MicroscopeMode? mode)
        {
            if (IsInDesignMode || mode == null || mode == LightSheet.Mode)
                return true;
            try
            {
                switch (mode)
                {
                    case MicroscopeMode.Preview:
                        Preview.StartPreview();
                        break;
                    case MicroscopeMode.Off:
                        LightSheet.Stop();
                        break;
                    default:
                        LightSheet.Stop();
                        // Volume scans need parameters only the step itself has, so such steps start the scan themselves
                        throw new NotSupportedException($"The shell cannot enter {mode} mode.");
                }
                StatusMessage = "";
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not start {mode}: {ex.Message}";
                return false;
            }
        }

        #endregion

        #region IDisposable

        override protected void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                _selectedStep?.Deactivate();
                foreach (var step in Steps)
                    step.Dispose();
                Positions.Dispose();
                if (!IsInDesignMode)
                    LightSheet.Stop();
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
