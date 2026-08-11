using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HyperViewer.Helpers
{
    /// <summary>
    /// 轻量 INotifyPropertyChanged 实现基类，替代 CommunityToolkit.Mvvm 的 ObservableObject。
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value)) return false;
            storage = value;
            RaisePropertyChanged(propertyName);
            return true;
        }

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
