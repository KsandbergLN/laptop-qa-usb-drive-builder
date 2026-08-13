using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace LaptopQaUsbBuilder;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopQAUsbBuilder", "Logs");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"Crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        File.WriteAllText(path, e.Exception.ToString());
        MessageBox.Show($"The application encountered an error.\n\nA crash log was saved to:\n{path}\n\n{e.Exception.Message}",
            "Laptop QA USB Drive Builder", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Current.Shutdown(1);
    }
}
