using ipp;
using MatjesImager.Hardware;
using MatjesUtils;
using System.Configuration;
using System.Data;
using System.Windows;

namespace MatjesImager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public Microscope? LightSheet { get; private set; }

        static App()
        {
            DispatcherHelper.Initialize();
            //core.ippInit();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            LightSheet = new Microscope();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            LightSheet?.Dispose();
            LightSheet = null;
        }
    }

}
