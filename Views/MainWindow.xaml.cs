using System;
using System.Windows;
using System.Windows.Media;
using SerialPortTool.Helpers;

namespace SerialPortTool.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        if (Application.Current.Resources["AppLogoImage"] is DrawingImage logo)
            AppIconHelper.ApplyWindowIcon(this, logo);
        Closing += OnClosing;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
        {
            // 为每个串口订阅 RequestClose 事件
            foreach (var port in vm.Ports)
            {
                port.RequestClose += OnPortRequestClose;
            }

            // 监听集合变化，为新添加的串口订阅事件
            vm.Ports.CollectionChanged += (_, args) =>
            {
                if (args.NewItems != null)
                {
                    foreach (ViewModels.SerialPortViewModel newPort in args.NewItems)
                    {
                        newPort.RequestClose += OnPortRequestClose;
                    }
                }
            };
        }
    }

    private void OnPortRequestClose(object? sender, EventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm && sender is ViewModels.SerialPortViewModel port)
        {
            vm.RemovePort(port);
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.Dispose();
        }
    }
}
