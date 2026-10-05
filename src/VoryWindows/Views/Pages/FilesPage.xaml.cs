using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VoryWindows.Models;
using VoryWindows.ViewModels;

namespace VoryWindows.Views.Pages
{
    public partial class FilesPage : UserControl
    {
        public FilesPage()
        {
            InitializeComponent();
            FileList.MouseDoubleClick += (s, e) =>
            {
                var vm = DataContext as FilesViewModel;
                var item = FileList.SelectedItem as FileItem;
                if (vm == null || item == null) return;
                if (item.IsDirectory && vm.NavigateCommand.CanExecute(item))
                    vm.NavigateCommand.Execute(item);
                else if (vm.DownloadCommand.CanExecute(item))
                    vm.DownloadCommand.Execute(item);
            };
        }

        private void Hidden_Toggled(object sender, RoutedEventArgs e)
        {
            (DataContext as FilesViewModel)?.RefreshAsync();
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            var vm = DataContext as FilesViewModel;
            if (files != null && vm != null)
                vm.UploadPathsAsync(files.Where(File.Exists).ToArray());
        }
    }
}
