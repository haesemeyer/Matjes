using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Threading;
using System.Threading.Tasks;
using NationalInstruments.DAQmx;
using MatjesUtils;
using MatjesImager.ViewModels;

namespace MatjesImager.Views
{
    /// <summary>
    /// Interaction logic for TestView.xaml
    /// </summary>
    public partial class TestView : WindowAwareView
    {
        TestViewModel viewModel;

        public TestView()
        {
            InitializeComponent();
            viewModel = this.ViewModel.Source as TestViewModel;
        }
    }
}
