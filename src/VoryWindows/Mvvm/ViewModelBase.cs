using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VoryWindows.Mvvm
{
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        protected void RunOnUi(Action action)
        {
            var disp = System.Windows.Application.Current?.Dispatcher;
            if (disp == null || disp.CheckAccess()) action();
            else disp.Invoke(action);
        }
    }
}
