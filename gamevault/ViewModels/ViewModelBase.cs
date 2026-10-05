using Avalonia.Threading;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace gamevault.ViewModels
{
    abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// WPF tolerated PropertyChanged from background threads (timers, Task.Run); Avalonia bindings must be
        /// updated on the UI thread, so notifications raised elsewhere are posted to it.
        /// </summary>
        protected void OnPropertyChanged([CallerMemberName] string name = "")
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
            else
            {
                Dispatcher.UIThread.Post(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
            }
        }
    }
}
