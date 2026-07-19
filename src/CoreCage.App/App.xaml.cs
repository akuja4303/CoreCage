using System.IO;
using System.Windows;

namespace CoreCage.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); // honors StartupUri (MainWindow)

        // Standalone app: no old-window safety net, so wire our own. Log unhandled exceptions to
        // corecage-crash.txt next to the exe and keep the UI alive where possible, instead of a
        // silent die — this is also what makes a headless/elevated launch diagnosable.
        DispatcherUnhandledException += (_, args) =>
        {
            Dump("Dispatcher", args.Exception);
            MessageBox.Show($"An error occurred: {args.Exception.Message}", "CoreCage",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Dump("AppDomain", args.ExceptionObject as Exception);

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Dump("Task", args.Exception);
            args.SetObserved();
        };
    }

    private static void Dump(string source, Exception? ex)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "corecage-crash.txt");
            File.AppendAllText(path,
                $"=== {source} @ {DateTime.Now:HH:mm:ss} ==={Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* diagnostics must never throw */ }
    }
}
