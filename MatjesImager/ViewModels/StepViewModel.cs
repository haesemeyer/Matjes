using MatjesImager.Hardware;
using MatjesUtils;
using System;
using System.Collections.Generic;
using System.Text;

namespace MatjesImager.ViewModels
{
    /// <summary>
    /// Progress marker of a workflow step shown in the step list
    /// </summary>
    public enum StepState { Open = 0, Done = 1, NeedsAttention = 2 }

    /// <summary>
    /// Base class of the ordered workflow steps hosted by the shell.
    /// Steps do not own hardware: they declare the microscope mode they need and the shell
    /// switches the shared microscope into that mode when the step is selected
    /// </summary>
    public abstract class StepViewModel : ViewModelBase
    {
        private StepState _state = StepState.Open;

        private string _stateHint = "";

        private bool _isActive;

        /// <param name="number">The 1-based position in the workflow</param>
        /// <param name="title">The name shown in the step list</param>
        /// <param name="requiredMode">The microscope mode the step runs in, or null if the step leaves the microscope as it is</param>
        protected StepViewModel(int number, string title, MicroscopeMode? requiredMode)
        {
            Number = number;
            Title = title;
            RequiredMode = requiredMode;
        }

        /// <summary>
        /// The 1-based position in the workflow
        /// </summary>
        public int Number { get; }

        /// <summary>
        /// The name shown in the step list
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// The microscope mode the step runs in, or null if the step leaves the microscope as it is
        /// </summary>
        public MicroscopeMode? RequiredMode { get; }

        /// <summary>
        /// Steps running in preview get the shared piezo, sheet z and sheet extent controls
        /// </summary>
        public bool UsesPositionControls => RequiredMode == MicroscopeMode.Preview;

        /// <summary>
        /// The marker shown in the step list
        /// </summary>
        public StepState State
        {
            get { return _state; }
            protected set { _state = value; RaisePropertyChanged(nameof(State)); }
        }

        /// <summary>
        /// Short explanation of the state, e.g. what a step is still missing. Shown as tooltip in the step list
        /// </summary>
        public string StateHint
        {
            get { return _stateHint; }
            protected set { _stateHint = value; RaisePropertyChanged(nameof(StateHint)); }
        }

        /// <summary>
        /// Indicates whether this is the selected step
        /// </summary>
        public bool IsActive
        {
            get { return _isActive; }
            private set { _isActive = value; RaisePropertyChanged(nameof(IsActive)); }
        }

        /// <summary>
        /// Called by the shell after the step was selected and the microscope is in the required mode
        /// </summary>
        internal void Activate()
        {
            IsActive = true;
            OnActivated();
        }

        /// <summary>
        /// Called by the shell before another step is selected or the shell closes
        /// </summary>
        internal void Deactivate()
        {
            if (!IsActive)
                return;
            OnDeactivated();
            IsActive = false;
        }

        protected virtual void OnActivated() { }

        protected virtual void OnDeactivated() { }
    }
}
