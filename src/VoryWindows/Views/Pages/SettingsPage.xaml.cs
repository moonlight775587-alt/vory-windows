using System.Windows;
using System.Windows.Controls;
using VoryWindows.ViewModels;

namespace VoryWindows.Views.Pages
{
    public partial class SettingsPage : UserControl
    {
        public SettingsPage()
        {
            InitializeComponent();
        }

        private void Accent_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var vm = DataContext as SettingsViewModel;
            if (button?.Tag is string color && vm != null)
                vm.Appearance.Accent = color;
        }
    }
}
