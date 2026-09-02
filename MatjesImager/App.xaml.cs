using ipp;
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
        static App()
        {
            DispatcherHelper.Initialize();
            //core.ippInit();
        }
    }

}
