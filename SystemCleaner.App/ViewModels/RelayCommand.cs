using System;
using System.Windows.Input;

namespace SystemCleaner.App.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;
    private event EventHandler? _canExecuteChanged;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        if (execute is null)
        {
            throw new ArgumentNullException(nameof(execute));
        }

        _execute = _ => execute();
        if (canExecute is not null)
        {
            _canExecute = _ => canExecute();
        }
    }

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add
        {
            _canExecuteChanged += value;
            if (_canExecute is not null)
            {
                CommandManager.RequerySuggested += value;
            }
        }
        remove
        {
            _canExecuteChanged -= value;
            if (_canExecute is not null)
            {
                CommandManager.RequerySuggested -= value;
            }
        }
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged()
    {
        CommandManager.InvalidateRequerySuggested();
        _canExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
