using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MatjesImager.ViewModels;

namespace MatjesImager.Views
{
    /// <summary>
    /// Interaction logic for VolumeScanView.xaml
    /// </summary>
    public partial class VolumeScanView : UserControl
    {
        /// <summary>
        /// The view model holding the volume scan settings and test results. Owned by the shell, which also disposes it
        /// </summary>
        public VolumeScanViewModel? VolumeScan => DataContext as VolumeScanViewModel;

        public VolumeScanView()
        {
            InitializeComponent();
        }

        private void Test_Click(object sender, RoutedEventArgs e)
        {
            VolumeScan?.StartTest();
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            VolumeScan?.StopScan();
        }

        private void AutoContrast_Click(object sender, RoutedEventArgs e)
        {
            VolumeScan?.AutoContrast();
        }

        private void Accept_Click(object sender, RoutedEventArgs e)
        {
            VolumeScan?.Accept();
        }
    }
}
