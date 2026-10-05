using System.Windows.Controls;
using VoryWindows.Models;
using VoryWindows.ViewModels;

namespace VoryWindows.Views.Pages
{
    public partial class ChatsPage : UserControl
    {
        public ChatsPage()
        {
            InitializeComponent();
            ChatList.SelectionChanged += (s, e) =>
            {
                var vm = DataContext as ChatsViewModel;
                var sel = ChatList.SelectedItem as ChatSession;
                if (vm != null && sel != null && vm.OpenCommand.CanExecute(sel))
                    vm.OpenCommand.Execute(sel);
            };
        }
    }
}
