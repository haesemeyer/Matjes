using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MatjesImager.ViewModels;

namespace MatjesImager.Views
{
    /// <summary>
    /// Interaction logic for PositionsView.xaml
    /// </summary>
    public partial class PositionsView : UserControl
    {
        private PositionsViewModel? Positions => DataContext as PositionsViewModel;

        public PositionsView()
        {
            InitializeComponent();
        }

        private void Z1Up_Click(object sender, RoutedEventArgs e) => Positions?.NudgeZ1(1);

        private void Z1Down_Click(object sender, RoutedEventArgs e) => Positions?.NudgeZ1(-1);

        private void Z2Up_Click(object sender, RoutedEventArgs e) => Positions?.NudgeZ2(1);

        private void Z2Down_Click(object sender, RoutedEventArgs e) => Positions?.NudgeZ2(-1);
    }
}
