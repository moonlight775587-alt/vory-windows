using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json.Linq;
using VoryWindows.Models;
using VoryWindows.Mvvm;
using VoryWindows.Services;
using VoryWindows.Views.Dialogs;

namespace VoryWindows.ViewModels
{
    public class BotsViewModel : ViewModelBase
    {
        private readonly AppState _state;
        private readonly MainViewModel _main;

        public ObservableCollection<ProfileInfo> Bots { get; } = new ObservableCollection<ProfileInfo>();
        public ObservableCollection<string> GroupRooms { get; } = new ObservableCollection<string>();

        public BotsViewModel(AppState state, MainViewModel main)
        {
            _state = state;
            _main = main;
            OpenChatsCommand = new RelayCommand(p =>
            {
                var b = p as ProfileInfo;
                if (b == null) return;
                _state.ActiveGateway.SelectedProfile = b.Name;
                _state.SaveGateways();
                _main.Page = AppPage.Chats;
                _main.Chats.RefreshAsync();
            });
            EditCommand = new RelayCommand(p => EditProfile(p as ProfileInfo));
            NewBotCommand = new AsyncRelayCommand(CreateBotAsync);
            DeleteCommand = new AsyncRelayCommand(p => DeleteBotAsync(p as ProfileInfo));
            CreateRoomCommand = new AsyncRelayCommand(CreateRoomAsync);
            RefreshCommand = new AsyncRelayCommand(() => RefreshAsync());
        }

        public RelayCommand OpenChatsCommand { get; }
        public RelayCommand EditCommand { get; }
        public AsyncRelayCommand NewBotCommand { get; }
        public AsyncRelayCommand DeleteCommand { get; }
        public AsyncRelayCommand CreateRoomCommand { get; }
        public AsyncRelayCommand RefreshCommand { get; }

        private bool _loading;
        public bool Loading
        {
            get { return _loading; }
            set { Set(ref _loading, value); }
        }

        public string BotColor(string name)
        {
            var g = _state.ActiveGateway;
            if (g != null && g.BotColors.TryGetValue(name, out var c)) return c;
            // Stable hash -> pleasant hue.
            int h = Math.Abs(name.GetHashCode()) % 360;
            return "hsl(" + h + ",55%,55%)";
        }

        public void SetBotColor(string name, string color)
        {
            var g = _state.ActiveGateway;
            if (g == null) return;
            g.BotColors[name] = color;
            _state.SaveGateways();
            OnPropertyChanged(nameof(Bots));
        }

        public async Task RefreshAsync()
        {
            if (_state.Rest == null) return;
            Loading = true;
            try
            {
                var tok = await _state.Rest.GetAsync("/api/profiles");
                RunOnUi(() =>
                {
                    Bots.Clear();
                    foreach (var t in HomeViewModel.AsArray(tok))
                    {
                        var o = t as JObject;
                        Bots.Add(new ProfileInfo
                        {
                            Name = o?["name"]?.ToString() ?? "?",
                            Description = o?["description"]?.ToString() ?? "",
                            Model = o?["model"]?.ToString() ?? o?["default_model"]?.ToString() ?? "",
                            Status = o?["status"]?.ToString() ?? ""
                        });
                    }
                });
                await LoadGroupsAsync();
            }
            catch (Exception ex) { Log.Warn("Bots refresh failed: " + ex.Message); }
            finally { Loading = false; }
        }

        private async Task LoadGroupsAsync()
        {
            try
            {
                var res = await _state.RpcAsync("groups.list", new JObject());
                RunOnUi(() =>
                {
                    GroupRooms.Clear();
                    foreach (var t in HomeViewModel.AsArray(res["groups"] ?? res))
                    {
                        var n = (t as JObject)?["name"]?.ToString() ?? t.ToString();
                        GroupRooms.Add(n);
                    }
                });
            }
            catch (InvalidOperationException) { /* -32601: no groups; hide section */ }
            catch { }
        }

        private void EditProfile(ProfileInfo b)
        {
            if (b == null) return;
            var dlg = new ProfileEditorWindow(_state, b);
            dlg.Owner = App.Current.MainWindow;
            dlg.ShowDialog();
            RefreshAsync();
        }

        private async Task CreateBotAsync()
        {
            var dlg = new InputDialog("New bot", "Bot name:");
            dlg.Owner = App.Current.MainWindow;
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value)) return;
            try
            {
                await _state.Rest.PostJsonAsync("/api/profiles",
                    new JObject { ["name"] = dlg.Value.Trim() });
                await RefreshAsync();
            }
            catch (Exception ex) { Log.Warn("Create bot failed: " + ex.Message); }
        }

        private async Task DeleteBotAsync(ProfileInfo b)
        {
            if (b == null) return;
            try
            {
                await _state.Rest.DeleteJsonAsync("/api/profiles/" + Uri.EscapeDataString(b.Name));
                await RefreshAsync();
            }
            catch (Exception ex) { Log.Warn("Delete bot failed: " + ex.Message); }
        }

        private async Task CreateRoomAsync()
        {
            var dlg = new InputDialog("New group room", "Room name:");
            dlg.Owner = App.Current.MainWindow;
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value)) return;
            try
            {
                await _state.RpcAsync("groups.create", new JObject { ["name"] = dlg.Value.Trim() });
                await LoadGroupsAsync();
            }
            catch (Exception ex) { Log.Warn("groups.create failed: " + ex.Message); }
        }
    }
}
