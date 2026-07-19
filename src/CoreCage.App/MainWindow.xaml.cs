using System.Windows;

namespace CoreCage.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;

    public MainWindow()
    {
        InitializeComponent();
        _shell = new ShellViewModel();
        DataContext = _shell;
        Closed += (_, _) => _shell.Dispose();

        // Land centered on the PRIMARY monitor (WPF's CenterScreen otherwise lands on whichever
        // screen Windows last used — it opened on the vertical side monitor). Position after layout.
        Loaded += (_, _) =>
        {
            Left = (SystemParameters.PrimaryScreenWidth  - ActualWidth)  / 2;
            Top  = (SystemParameters.PrimaryScreenHeight - ActualHeight) / 2;
        };
    }
}
