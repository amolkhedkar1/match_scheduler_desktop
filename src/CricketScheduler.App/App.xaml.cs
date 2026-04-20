using System.Windows;

namespace CricketScheduler.App;

public partial class App : Application
{
    public App()
    {
        this.DispatcherUnhandledException += (s, e) =>
        {
            MessageBox.Show(e.Exception.ToString(), "Unhandled Exception");
            e.Handled = true;
        };
    }

}
