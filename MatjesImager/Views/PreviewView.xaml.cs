using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MatjesImager.ViewModels;

namespace MatjesImager.Views
{
    /// <summary>
    /// Interaction logic for PreviewView.xaml
    /// </summary>
    public partial class PreviewView : UserControl
    {
        /// <summary>
        /// The view model holding the preview settings. Owned by the shell, which also disposes it
        /// </summary>
        public PreviewViewModel? Preview => DataContext as PreviewViewModel;

        public PreviewView()
        {
            InitializeComponent();
        }

        private void ApplyFrameRate_Click(object sender, RoutedEventArgs e)
        {
            Preview?.ApplyFrameRate();
        }

        private void AutoContrast_Click(object sender, RoutedEventArgs e)
        {
            Preview?.AutoContrast();
        }
    }
}
