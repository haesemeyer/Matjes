using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MatjesImager.ViewModels;

namespace MatjesImager.Views
{
    /// <summary>
    /// Interaction logic for ZCalibrationView.xaml
    /// </summary>
    public partial class ZCalibrationView : UserControl
    {
        /// <summary>
        /// The view model holding calibration state and results. Owned by the shell, which also disposes it
        /// </summary>
        public ZCalibrationViewModel? Calibration => DataContext as ZCalibrationViewModel;

        public ZCalibrationView()
        {
            InitializeComponent();
        }

        private void AddPoint_Click(object sender, RoutedEventArgs e)
        {
            Calibration?.AddCalibrationPoint();
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            Calibration?.ClearCalibrationPoints();
        }

        private void Accept_Click(object sender, RoutedEventArgs e)
        {
            Calibration?.AcceptCalibration();
        }

        private void GoToPoint_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CalibrationPoint point)
                Calibration?.GoToCalibrationPoint(point);
        }

        private void ClearPoint_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is CalibrationPoint point)
                Calibration?.RemoveCalibrationPoint(point);
        }
    }
}
