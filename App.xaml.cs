using System.Configuration;
using System.Data;
using System.Windows;

namespace AppFlow;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        AppInstance.ListenForActivation(() => Dispatcher.BeginInvoke(new Action(window.ActivateFromSecondLaunch)));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppInstance.StopListening();
        base.OnExit(e);
    }
}

