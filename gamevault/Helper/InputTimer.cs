using Avalonia.Threading;

namespace gamevault.Helper
{
    /// <summary>
    /// Debounce timer that carries the last input value.
    /// </summary>
    public class InputTimer : DispatcherTimer
    {
        public string Data;
    }
}
