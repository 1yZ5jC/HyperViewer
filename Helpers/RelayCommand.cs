using System;
using System.Windows.Input;

namespace HyperViewer.Helpers
{
    /// <summary>
    /// 轻量 ICommand 实现，替代 CommunityToolkit.Mvvm 的 RelayCommand。
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();
        public void Execute(object parameter) => _execute();

        public event EventHandler CanExecuteChanged;
        public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 带 command parameter 的 RelayCommand<T>。
    /// </summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            if (_canExecute == null) return true;
            T arg = (parameter is T t) ? t : default;
            return _canExecute(arg);
        }

        public void Execute(object parameter)
        {
            T arg = (parameter is T t) ? t : default;
            _execute(arg);
        }

        public event EventHandler CanExecuteChanged;
        public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 异步 ICommand 实现，用于绑定异步操作 (例如打开文件)。
    /// </summary>
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<System.Threading.Tasks.Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isRunning;

        public AsyncRelayCommand(Func<System.Threading.Tasks.Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => !_isRunning && (_canExecute == null || _canExecute());
        public async void Execute(object parameter)
        {
            _isRunning = true;
            RaiseCanExecuteChanged();
            try { await _execute(); }
            finally { _isRunning = false; RaiseCanExecuteChanged(); }
        }

        public event EventHandler CanExecuteChanged;
        private void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
