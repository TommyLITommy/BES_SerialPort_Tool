using System.Windows;
using SerialPortTool.Helpers;
using SerialPortTool.Views;

namespace SerialPortTool;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (AppIconHelper.TryGenerateAppIcoFromArgs(e.Args))
        {
            Shutdown(0);
            return;
        }

        base.OnStartup(e);
        new MainWindow().Show();
    }
}
