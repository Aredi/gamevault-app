using Avalonia.Controls;
using Avalonia.Interactivity;
using gamevault.Helper;
using gamevault.Helper.Platform;
using System.IO;

namespace gamevault.Windows
{
    public partial class ExceptionWindow : Window
    {
        public ExceptionWindow()
        {
            InitializeComponent();
        }

        private void OpenLog_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(ProfileManager.ErrorLogDir))
            {
                PlatformInfo.OpenFolder(ProfileManager.ErrorLogDir);
            }
            this.Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
