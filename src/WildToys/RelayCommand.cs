using System;
using System.Windows.Input;

namespace WildToys;

/// <summary>Minimal ICommand that always executes and takes no parameter.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute();
}
