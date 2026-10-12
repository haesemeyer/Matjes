using MatjesImager.Hardware;
using System;
using System.Collections.Generic;
using System.Text;

namespace MatjesImager.ViewModels
{
    /// <summary>
    /// Stands in for a workflow step whose panel has not been built yet so that the step list shows the whole workflow
    /// </summary>
    public class PlaceholderStepViewModel : StepViewModel
    {
        public PlaceholderStepViewModel(int number, string title, MicroscopeMode? requiredMode, string description)
            : base(number, title, requiredMode)
        {
            Description = description;
        }

        /// <summary>
        /// What the step will do and what can be done in the meantime
        /// </summary>
        public string Description { get; }
    }
}
