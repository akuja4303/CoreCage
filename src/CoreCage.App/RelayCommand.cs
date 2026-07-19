using System.Windows.Input;

namespace CoreCage.App;

/// <summary>
/// Minimal async-aware ICommand so buttons can bind to a Task-returning action
/// without pulling in an MVVM toolkit for a one-screen skeleton.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Func<Task> _executeAsync;
    private readonly Func<bool> _canExecute;

    public RelayCommand(Func<Task> executeAsync, Func<bool> canExecute)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute();

    public async void Execute(object? parameter) => await _executeAsync().ConfigureAwait(true);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
