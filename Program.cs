using Velopack;

namespace AppFlow;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        using var instanceMutex = new Mutex(true, AppInstance.InstanceMutexName, out var firstInstance);
        if (!firstInstance)
        {
            AppInstance.RequestActivation();
            return;
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
