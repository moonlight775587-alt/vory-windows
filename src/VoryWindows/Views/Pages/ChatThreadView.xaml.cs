using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using VoryWindows.ViewModels;

namespace VoryWindows.Views.Pages
{
    public partial class ChatThreadView : UserControl
    {
        private bool _nearBottom = true;

        public ChatThreadView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            ThreadScroller.ScrollChanged += (s, e) =>
            {
                _nearBottom = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 40;
            };
            Loaded += (s, e) => HookItems();
            SuggestionsList.SelectionChanged += (s, e) =>
            {
                if (SuggestionsList.SelectedItem is string sel)
                {
                    SuggestionsList.SelectedItem = null;
                    Vm?.SuggestionCommand.Execute(sel);
                }
            };
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            HookItems();
            var vm = DataContext as ChatThreadViewModel;
            if (vm != null) vm.AttachRequested += OnAttachRequested;
        }

        private void HookItems()
        {
            var incc = ThreadItems.ItemsSource as INotifyCollectionChanged;
            if (incc != null)
                incc.CollectionChanged += (s, e) =>
                {
                    if (_nearBottom || e.Action == NotifyCollectionChangedAction.Reset)
                        Dispatcher.BeginInvoke(new Action(() => ThreadScroller.ScrollToBottom()));
                };
        }

        private ChatThreadViewModel Vm { get { return DataContext as ChatThreadViewModel; } }

        private void OnAttachRequested()
        {
            var dlg = new OpenFileDialog { Multiselect = true };
            if (dlg.ShowDialog() == true)
                foreach (var f in dlg.FileNames) Vm?.AddAttachment(f);
        }

        private void ComposerBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                if (Vm != null && Vm.SendCommand.CanExecute(null)) Vm.SendCommand.Execute(null);
            }
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (Clipboard.ContainsImage())
                {
                    e.Handled = true;
                    try
                    {
                        var img = Clipboard.GetImage();
                        var tmp = Path.Combine(Path.GetTempPath(), "vory-paste-" + Guid.NewGuid().ToString("N") + ".png");
                        using (var fs = File.Create(tmp))
                        {
                            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
                            enc.Save(fs);
                        }
                        Vm?.AddAttachment(tmp);
                    }
                    catch { /* fall through to normal paste */ }
                }
            }
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null)
                    foreach (var f in files.Where(File.Exists)) Vm?.AddAttachment(f);
            }
        }

        private void Suggestions_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var lb = sender as ListBox;
            if (lb?.SelectedItem is string s) Vm?.SuggestionCommand.Execute(s);
        }

        private void PromptAnswer_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var item = button?.DataContext as ChatItem;
            if (item == null) return;
            // Find the input box inside the item template (TextBox or PasswordBox named PromptInput/PromptSecret).
            var presenter = FindAncestor<ContentPresenter>(button);
            string value = "";
            if (presenter != null)
            {
                var tb = presenter.ContentTemplate.FindName("PromptInput", presenter) as TextBox;
                var pb = presenter.ContentTemplate.FindName("PromptSecret", presenter) as PasswordBox;
                if (tb != null && tb.Visibility == Visibility.Visible) value = tb.Text;
                else if (pb != null && pb.Visibility == Visibility.Visible) value = pb.Password;
            }
            item.ToolCommand = value; // never logged for secrets
            Vm?.AnswerPromptCommand.Execute(item);
            // Clear the secret from the visual immediately.
            if (presenter != null)
            {
                var pb = presenter.ContentTemplate.FindName("PromptSecret", presenter) as PasswordBox;
                if (pb != null) pb.Clear();
            }
        }

        private static T FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null)
            {
                if (d is T) return (T)d;
                d = VisualTreeHelper.GetParent(d);
            }
            return null;
        }
    }
}
