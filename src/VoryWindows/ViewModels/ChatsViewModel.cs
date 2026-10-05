using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using Newtonsoft.Json.Linq;
using VoryWindows.Models;
using VoryWindows.Mvvm;
using VoryWindows.Services;

namespace VoryWindows.ViewModels
{
    public class ChatsViewModel : ViewModelBase
    {
        private readonly AppState _state;
        private readonly MainViewModel _main;

        public ObservableCollection<ChatSession> Sessions { get; } = new ObservableCollection<ChatSession>();
        public ListCollectionView Filtered { get; }

        public ChatsViewModel(AppState state, MainViewModel main)
        {
            _state = state;
            _main = main;
            Filtered = (ListCollectionView)CollectionViewSource.GetDefaultView(Sessions);
            Filtered.Filter = FilterSession;
            Filtered.SortDescriptions.Add(new System.ComponentModel.SortDescription(
                nameof(ChatSession.UpdatedAt), System.ComponentModel.ListSortDirection.Descending));

            OpenCommand = new RelayCommand(p => { var s = p as ChatSession; if (s != null) _main.OpenChat(s.Id, s.Profile); });
            NewChatCommand = new AsyncRelayCommand(NewChatAsync);
            DeleteCommand = new AsyncRelayCommand(p => DeleteAsync(p as ChatSession));
            PinCommand = new RelayCommand(p => TogglePin(p as ChatSession));
            ArchiveCommand = new RelayCommand(p => ToggleArchive(p as ChatSession));
            RefreshCommand = new AsyncRelayCommand(() => RefreshAsync());
        }

        private string _search = "";
        public string Search
        {
            get { return _search; }
            set { Set(ref _search, value); Filtered.Refresh(); }
        }

        private string _projectFilter = "";
        public string ProjectFilter
        {
            get { return _projectFilter; }
            set { Set(ref _projectFilter, value); Filtered.Refresh(); }
        }

        public ObservableCollection<string> Projects { get; } = new ObservableCollection<string>();

        private bool FilterSession(object o)
        {
            var s = o as ChatSession;
            if (s == null) return false;
            var g = _state.ActiveGateway;
            if (g != null && g.ArchivedSessions.Contains(s.Id)) return false;
            if (!string.IsNullOrWhiteSpace(_projectFilter) && _projectFilter != "(all)"
                && !string.Equals(s.Project, _projectFilter, StringComparison.OrdinalIgnoreCase)) return false;
            if (string.IsNullOrWhiteSpace(_search)) return true;
            var q = _search.ToLowerInvariant();
            return (s.Title ?? "").ToLowerInvariant().Contains(q)
                || (s.Preview ?? "").ToLowerInvariant().Contains(q)
                || (s.Profile ?? "").ToLowerInvariant().Contains(q);
        }

        public RelayCommand OpenCommand { get; }
        public AsyncRelayCommand NewChatCommand { get; }
        public AsyncRelayCommand DeleteCommand { get; }
        public RelayCommand PinCommand { get; }
        public RelayCommand ArchiveCommand { get; }
        public AsyncRelayCommand RefreshCommand { get; }

        public bool IsPinned(ChatSession s)
        {
            var g = _state.ActiveGateway;
            return g != null && g.PinnedSessions.Contains(s.Id);
        }

        private void TogglePin(ChatSession s)
        {
            var g = _state.ActiveGateway;
            if (s == null || g == null) return;
            if (!g.PinnedSessions.Add(s.Id)) g.PinnedSessions.Remove(s.Id);
            _state.SaveGateways();
            Filtered.Refresh();
        }

        private void ToggleArchive(ChatSession s)
        {
            var g = _state.ActiveGateway;
            if (s == null || g == null) return;
            if (!g.ArchivedSessions.Add(s.Id)) g.ArchivedSessions.Remove(s.Id);
            _state.SaveGateways();
            Filtered.Refresh();
        }

        private async Task DeleteAsync(ChatSession s)
        {
            if (s == null || _state.Rest == null) return;
            try
            {
                await _state.Rest.DeleteJsonAsync("/api/sessions/" + Uri.EscapeDataString(s.Id));
                Sessions.Remove(s);
            }
            catch (Exception ex)
            {
                Log.Warn("Delete session failed: " + ex.Message);
            }
        }

        public void MarkNeedsYou(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            RunOnUi(() =>
            {
                var s = Sessions.FirstOrDefault(x => x.Id == sessionId);
                if (s != null) { s.NeedsYou = true; Filtered.Refresh(); }
            });
        }

        public void ClearNeedsYou(string sessionId)
        {
            RunOnUi(() =>
            {
                var s = Sessions.FirstOrDefault(x => x.Id == sessionId);
                if (s != null) { s.NeedsYou = false; Filtered.Refresh(); }
            });
        }

        public async Task RefreshAsync()
        {
            if (_state.Rest == null) return;
            try
            {
                var tok = await _state.Rest.GetAsync("/api/sessions",
                    new Dictionary<string, string> { { "order", "recent" } });
                RunOnUi(() =>
                {
                    Sessions.Clear();
                    foreach (var t in HomeViewModel.AsArray(tok))
                        Sessions.Add(HomeViewModel.ParseSession(t as JObject));
                });
                await LoadProjectsAsync();
            }
            catch (Exception ex) { Log.Warn("Sessions refresh failed: " + ex.Message); }
        }

        private async Task LoadProjectsAsync()
        {
            try
            {
                var res = await _state.RpcAsync("projects.list", new JObject());
                var names = new List<string> { "(all)" };
                foreach (var t in HomeViewModel.AsArray(res["projects"] ?? res))
                {
                    var n = (t as JObject)?["name"]?.ToString();
                    if (!string.IsNullOrEmpty(n)) names.Add(n);
                }
                RunOnUi(() =>
                {
                    Projects.Clear();
                    foreach (var n in names) Projects.Add(n);
                    if (string.IsNullOrEmpty(_projectFilter)) ProjectFilter = "(all)";
                });
            }
            catch (InvalidOperationException)
            {
                // -32601: gateway has no projects; hide the filter.
                RunOnUi(() => Projects.Clear());
            }
            catch { }
        }

        private async Task NewChatAsync()
        {
            if (_state.Socket == null) return;
            try
            {
                var res = await _state.RpcAsync("session.create", new JObject());
                var id = res["session_id"]?.ToString() ?? res["id"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(id))
                {
                    await RefreshAsync();
                    _main.OpenChat(id, _state.ActiveGateway?.SelectedProfile ?? "");
                }
            }
            catch (Exception ex) { Log.Warn("session.create failed: " + ex.Message); }
        }
    }
}
