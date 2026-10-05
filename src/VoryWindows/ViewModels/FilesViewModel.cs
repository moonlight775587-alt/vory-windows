using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using VoryWindows.Models;
using VoryWindows.Mvvm;
using VoryWindows.Services;

namespace VoryWindows.ViewModels
{
    public class FilesViewModel : ViewModelBase
    {
        private readonly AppState _state;

        public ObservableCollection<FileItem> Items { get; } = new ObservableCollection<FileItem>();

        public FilesViewModel(AppState state)
        {
            _state = state;
            NavigateCommand = new RelayCommand(p => Navigate(p as FileItem));
            UpCommand = new RelayCommand(() => NavigateUp(), () => !string.IsNullOrEmpty(CurrentPath));
            RefreshCommand = new AsyncRelayCommand(() => RefreshAsync());
            UploadCommand = new AsyncRelayCommand(UploadAsync);
            DownloadCommand = new AsyncRelayCommand(p => DownloadAsync(p as FileItem));
            ToggleHiddenCommand = new RelayCommand(() => { ShowHidden = !ShowHidden; RefreshAsync(); });
        }

        private string _currentPath = "";
        public string CurrentPath
        {
            get { return _currentPath; }
            set { Set(ref _currentPath, value); UpCommand.RaiseCanExecuteChanged(); }
        }

        private bool _showHidden;
        public bool ShowHidden
        {
            get { return _showHidden; }
            set { Set(ref _showHidden, value); }
        }

        private bool _loading;
        public bool Loading
        {
            get { return _loading; }
            set { Set(ref _loading, value); }
        }

        private string _status = "";
        public string Status
        {
            get { return _status; }
            set { Set(ref _status, value); }
        }

        private int _page;
        private bool _hasMore = true;

        public RelayCommand NavigateCommand { get; }
        public RelayCommand UpCommand { get; }
        public AsyncRelayCommand RefreshCommand { get; }
        public AsyncRelayCommand UploadCommand { get; }
        public AsyncRelayCommand DownloadCommand { get; }
        public RelayCommand ToggleHiddenCommand { get; }

        public async Task RefreshAsync()
        {
            _page = 0;
            _hasMore = true;
            RunOnUi(() => Items.Clear());
            await LoadMoreAsync();
        }

        public async Task LoadMoreAsync()
        {
            if (_state.Rest == null || !_hasMore || _loading) return;
            _loading = true;
            try
            {
                var tok = await _state.Rest.GetAsync("/api/files",
                    new Dictionary<string, string>
                    {
                        { "path", _currentPath },
                        { "page", _page.ToString() },
                        { "per_page", "100" }
                    });
                var list = HomeViewModel.AsArray(
                    (tok as JObject)?["files"] ?? (tok as JObject)?["entries"] ?? tok).ToList();
                if (list.Count == 0) _hasMore = false;
                RunOnUi(() =>
                {
                    foreach (var t in list)
                    {
                        var o = t as JObject;
                        if (o == null) continue;
                        var name = o["name"]?.ToString() ?? "";
                        if (!_showHidden && name.StartsWith(".")) continue;
                        Items.Add(new FileItem
                        {
                            Name = name,
                            Path = o["path"]?.ToString() ?? name,
                            IsDirectory = o["is_dir"]?.Value<bool>() ?? o["isDirectory"]?.Value<bool>()
                                           ?? o["type"]?.ToString() == "directory",
                            Size = o["size"]?.Value<long>() ?? 0,
                            Modified = o["modified"]?.ToString() ?? o["mtime"]?.ToString() ?? ""
                        });
                    }
                });
                _page++;
            }
            catch (Exception ex)
            {
                Status = "Listing failed: " + ex.Message;
                _hasMore = false;
            }
            finally { _loading = false; }
        }

        private void Navigate(FileItem item)
        {
            if (item == null || !item.IsDirectory) return;
            CurrentPath = item.Path;
            RefreshAsync();
        }

        private void NavigateUp()
        {
            var p = (CurrentPath ?? "").TrimEnd('/');
            var i = p.LastIndexOf('/');
            CurrentPath = i <= 0 ? "" : p.Substring(0, i);
            RefreshAsync();
        }

        private async Task UploadAsync()
        {
            var dlg = new OpenFileDialog { Multiselect = true };
            if (dlg.ShowDialog() != true) return;
            await UploadPathsAsync(dlg.FileNames);
        }

        public async Task UploadPathsAsync(string[] paths)
        {
            if (_state.Rest == null) return;
            foreach (var path in paths)
            {
                try
                {
                    using (var fs = File.OpenRead(path))
                    {
                        await _state.Rest.UploadFileAsync(CurrentPath, Path.GetFileName(path), fs,
                            MimeGuess(Path.GetExtension(path)));
                    }
                    Status = "Uploaded " + Path.GetFileName(path);
                }
                catch (Exception ex) { Status = "Upload failed: " + ex.Message; }
            }
            await RefreshAsync();
        }

        private static string MimeGuess(string ext)
        {
            switch ((ext ?? "").ToLowerInvariant())
            {
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".pdf": return "application/pdf";
                case ".txt": return "text/plain";
                default: return "application/octet-stream";
            }
        }

        private async Task DownloadAsync(FileItem item)
        {
            if (item == null || item.IsDirectory || _state.Rest == null) return;
            var dlg = new SaveFileDialog { FileName = item.Name };
            if (dlg.ShowDialog() != true) return;
            try
            {
                using (var fs = File.Create(dlg.FileName))
                    await _state.Rest.DownloadFileAsync(item.Path, fs);
                Status = "Saved to " + dlg.FileName;
            }
            catch (Exception ex) { Status = "Download failed: " + ex.Message; }
        }
    }
}
