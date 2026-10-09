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
        /// <summary>
        /// The microscope instance owned by the running application
        /// </summary>
        private Microscope? _lightSheet;

        /// <summary>
        /// Hardware-free stand-in returned at design time so that the XAML designer can resolve bindings
        /// </summary>
        private static Microscope? _designLightSheet;

        /// <summary>
        /// The shared microscope. Created in OnStartup and disposed in OnExit.
        /// At design time a stand-in without hardware access is returned instead.
        /// Throws if accessed while the application does not own a microscope (before startup or after exit)
        /// </summary>
        public static Microscope LightSheet
        {
            get
            {
                if (ViewModelBase.IsInDesignMode)
                    return _designLightSheet ??= Microscope.CreateDesignInstance();
                return (Current as App)?._lightSheet
                    ?? throw new InvalidOperationException("The microscope is not available. It only exists between application startup and exit.");
            }
        }

        static App()
        {
            DispatcherHelper.Initialize();
            //core.ippInit();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _lightSheet = new Microscope();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            _lightSheet?.Dispose();
            _lightSheet = null;
        }
    }

}
