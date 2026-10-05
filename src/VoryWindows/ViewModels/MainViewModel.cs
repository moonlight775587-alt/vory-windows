using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VoryWindows.Mvvm;
using VoryWindows.Services;
using VoryWindows.Views.Pages;

namespace VoryWindows.ViewModels
{
    public enum AppPage { Home, Chats, Bots, Files, Settings }

    public class MainViewModel : ViewModelBase
    {
        private readonly AppState _state;

        private AppPage _page = AppPage.Chats;
        public AppPage Page
        {
            get { return _page; }
            set
            {
                if (Set(ref _page, value))
                {
                    UpdateViews();
                    if (value == AppPage.Home) Home.RefreshAsync();
                    if (value == AppPage.Bots) Bots.RefreshAsync();
                    if (value == AppPage.Files) Files.RefreshAsync();
                }
            }
        }

        private object _sidebar;
        public object Sidebar
        {
            get { return _sidebar; }
            set { Set(ref _sidebar, value); }
        }

        private object _content;
        public object Content
        {
            get { return _content; }
            set { Set(ref _content, value); }
        }

        private GridLength _sidebarWidth = new GridLength(300);
        public GridLength SidebarWidth
        {
            get { return _sidebarWidth; }
            set { Set(ref _sidebarWidth, value); }
        }

        public HomeViewModel Home { get; }
        public ChatsViewModel Chats { get; }
        public ChatThreadViewModel Thread { get; }
        public BotsViewModel Bots { get; }
        public FilesViewModel Files { get; }
        public SettingsViewModel Settings { get; }

        public RelayCommand NavigateCommand { get; }

        private readonly ChatsPage _chatsPage;
        private readonly ChatThreadView _threadView;
        private readonly HomePage _homePage;
        private readonly BotsPage _botsPage;
        private readonly FilesPage _filesPage;
        private readonly SettingsPage _settingsPage;

        public MainViewModel(AppState state)
        {
            _state = state;
            _state.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AppState.RestartRequired)
                    || e.PropertyName == nameof(AppState.RestartDetail))
                    OnPropertyChanged(e.PropertyName);
            };
            Home = new HomeViewModel(state, this);
            Chats = new ChatsViewModel(state, this);
            Thread = new ChatThreadViewModel(state, this);
            Bots = new BotsViewModel(state, this);
            Files = new FilesViewModel(state);
            Settings = new SettingsViewModel(state);

            _chatsPage = new ChatsPage { DataContext = Chats };
            _threadView = new ChatThreadView { DataContext = Thread };
            _homePage = new HomePage { DataContext = Home };
            _botsPage = new BotsPage { DataContext = Bots };
            _filesPage = new FilesPage { DataContext = Files };
            _settingsPage = new SettingsPage { DataContext = Settings };

            NavigateCommand = new RelayCommand(p =>
            {
                if (p is string) Page = (AppPage)Enum.Parse(typeof(AppPage), (string)p);
            });

            state.ApprovalRequested += (s, ap) => Chats.MarkNeedsYou(ap.SessionId);
            UpdateViews();
            UpdateHermesCommand = new AsyncRelayCommand(() => MaintenanceActionAsync("update"));
            RestartGatewayCommand = new AsyncRelayCommand(() => MaintenanceActionAsync("restart"));
            Task.Run(async () =>
            {
                await state.ConnectAsync();
                await state.ProbeRestartRequiredAsync();
                RunOnUi(() => Chats.RefreshAsync());
            });
        }

        public bool RestartRequired { get { return _state.RestartRequired; } }
        public string RestartDetail { get { return _state.RestartDetail; } }
        public AsyncRelayCommand UpdateHermesCommand { get; }
        public AsyncRelayCommand RestartGatewayCommand { get; }

        private async System.Threading.Tasks.Task MaintenanceActionAsync(string which)
        {
            if (_state.Rest == null) return;
            try
            {
                if (which == "update")
                    await _state.Rest.PostJsonAsync("/api/hermes/update", new Newtonsoft.Json.Linq.JObject());
                else
                    await _state.Rest.PostJsonAsync("/api/gateway/restart", new Newtonsoft.Json.Linq.JObject());
                await System.Threading.Tasks.Task.Delay(5000);
                await _state.DisconnectAsync();
                await _state.ConnectAsync();
                await _state.ProbeRestartRequiredAsync();
            }
            catch (Exception ex)
            {
                Services.Log.Warn("Maintenance action failed: " + ex.Message);
            }
        }

        private void UpdateViews()
        {
            switch (_page)
            {
                case AppPage.Home:
                    Sidebar = null; SidebarWidth = new GridLength(0); Content = _homePage; break;
                case AppPage.Chats:
                    Sidebar = _chatsPage; SidebarWidth = new GridLength(300); Content = _threadView; break;
                case AppPage.Bots:
                    Sidebar = null; SidebarWidth = new GridLength(0); Content = _botsPage; break;
                case AppPage.Files:
                    Sidebar = null; SidebarWidth = new GridLength(0); Content = _filesPage; break;
                case AppPage.Settings:
                    Sidebar = null; SidebarWidth = new GridLength(0); Content = _settingsPage; break;
            }
        }

        public void OpenChat(string sessionId, string profile)
        {
            Page = AppPage.Chats;
            Thread.OpenSessionAsync(sessionId, profile);
        }
    }
}
