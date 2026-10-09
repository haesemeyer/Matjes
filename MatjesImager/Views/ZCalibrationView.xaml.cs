using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MatjesUtils;
using MatjesImager.ViewModels;

namespace MatjesImager.Views
{
    /// <summary>
    /// Interaction logic for ZCalibrationView.xaml
    /// </summary>
    public partial class ZCalibrationView : WindowAwareView
    {
        ZCalibrationViewModel? _viewModel;

        /// <summary>
        /// The view model holding calibration state and results
        /// </summary>
        public ZCalibrationViewModel? Calibration => _viewModel;

        public ZCalibrationView()
        {
            InitializeComponent();
            _viewModel = this.ViewModel.Source as ZCalibrationViewModel;
        }

        protected override void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _viewModel?.Dispose();
            base.WindowClosing(sender, e);
        }

        private void AddPoint_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.AddCalibrationPoint();
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.ClearCalibrationPoints();
        }

        private void Accept_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.AcceptCalibration();
        }

        private void GoToPoint_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CalibrationPoint point)
                _viewModel?.GoToCalibrationPoint(point);
        }

        private void ClearPoint_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CalibrationPoint point)
                _viewModel?.RemoveCalibrationPoint(point);
        }

        private void Z1Up_Click(object sender, RoutedEventArgs e) => _viewModel?.NudgeZ1(1);

        private void Z1Down_Click(object sender, RoutedEventArgs e) => _viewModel?.NudgeZ1(-1);

        private void Z2Up_Click(object sender, RoutedEventArgs e) => _viewModel?.NudgeZ2(1);

        private void Z2Down_Click(object sender, RoutedEventArgs e) => _viewModel?.NudgeZ2(-1);
    }
}
