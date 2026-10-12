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
    /// Interaction logic for ShellView.xaml: step list, shared imaging view and the selected step's panel
    /// </summary>
    public partial class ShellView : WindowAwareView
    {
        ShellViewModel? _viewModel;

        public ShellView()
        {
            InitializeComponent();
            _viewModel = this.ViewModel.Source as ShellViewModel;
        }

        protected override void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _viewModel?.Dispose();
            base.WindowClosing(sender, e);
        }
    }
}
