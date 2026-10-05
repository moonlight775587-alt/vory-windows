using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using VoryWindows.Models;
using VoryWindows.Mvvm;
using VoryWindows.Services;

namespace VoryWindows.ViewModels
{
    public class UsageDay
    {
        public int Sessions { get; set; }
        public int Messages { get; set; }
        public long Tokens { get; set; }
    }

    public class HomeViewModel : ViewModelBase
    {
        private readonly AppState _state;
        private readonly MainViewModel _main;

        public HomeViewModel(AppState state, MainViewModel main)
        {
            _state = state;
            _main = main;
            SetDaysCommand = new RelayCommand(p => { Days = int.Parse((string)p); RefreshAsync(); });
            OpenChatCommand = new RelayCommand(p =>
            {
                var s = p as ChatSession;
                if (s != null) _main.OpenChat(s.Id, s.Profile);
            });
            OpenBotsCommand = new RelayCommand(() => _main.Page = AppPage.Bots);
        }

        private int _days = 30;
        public int Days
        {
            get { return _days; }
            set { Set(ref _days, value); }
        }

        private bool _loading;
        public bool Loading
        {
            get { return _loading; }
            set { Set(ref _loading, value); }
        }

        private string _greeting = "Hello";
        public string Greeting
        {
            get { return _greeting; }
            set { Set(ref _greeting, value); }
        }

        private string _error = "";
        public string Error
        {
            get { return _error; }
            set { Set(ref _error, value); }
        }

        // Overview numbers
        private int _sessions, _messages, _activeDays, _peakHour;
        private long _tokens;
        private string _topModel = "—", _costEstimate = "—";
        public int Sessions { get { return _sessions; } set { Set(ref _sessions, value); } }
        public int Messages { get { return _messages; } set { Set(ref _messages, value); } }
        public long Tokens { get { return _tokens; } set { Set(ref _tokens, value); } }
        public int ActiveDays { get { return _activeDays; } set { Set(ref _activeDays, value); } }
        public int PeakHour { get { return _peakHour; } set { Set(ref _peakHour, value); } }
        public string TopModel { get { return _topModel; } set { Set(ref _topModel, value); } }
        public string CostEstimate { get { return _costEstimate; } set { Set(ref _costEstimate, value); } }
        public string PeakHourLabel { get { return _peakHour < 0 ? "—" : _peakHour.ToString("D2") + ":00"; } }

        public ObservableCollection<int> ActivityBlocks { get; } = new ObservableCollection<int>();
        public ObservableCollection<ProfileInfo> Bots { get; } = new ObservableCollection<ProfileInfo>();
        public ObservableCollection<ChatSession> Recent { get; } = new ObservableCollection<ChatSession>();

        public RelayCommand SetDaysCommand { get; }
        public RelayCommand OpenChatCommand { get; }
        public RelayCommand OpenBotsCommand { get; }

        public async void RefreshAsync()
        {
            if (_state.Rest == null) return;
            Loading = true;
            Error = "";
            try
            {
                var hour = DateTime.Now.Hour;
                Greeting = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
                if (!string.IsNullOrWhiteSpace(_state.UserName)) Greeting += ", " + _state.UserName;

                // Overview
                try
                {
                    var usage = await _state.Rest.GetAsync("/api/analytics/usage",
                        new Dictionary<string, string> { { "days", _days.ToString() } });
                    ParseUsage(usage as JObject);
                }
                catch (Exception ex) { Error = "Overview: " + ex.Message; }

                // Bots row
                Bots.Clear();
                try
                {
                    var profiles = await _state.Rest.GetAsync("/api/profiles");
                    foreach (var t in AsArray(profiles))
                        Bots.Add(new ProfileInfo
                        {
                            Name = t["name"]?.ToString() ?? t["id"]?.ToString() ?? "?",
                            Description = t["description"]?.ToString() ?? "",
                            Model = t["model"]?.ToString() ?? t["default_model"]?.ToString() ?? "",
                            Status = t["status"]?.ToString() ?? ""
                        });
                }
                catch { }

                // Pick up where you left off
                Recent.Clear();
                try
                {
                    var sessions = await _state.Rest.GetAsync("/api/sessions",
                        new Dictionary<string, string> { { "order", "recent" }, { "limit", "100" } });
                    int n = 0;
                    foreach (var t in AsArray(sessions))
                    {
                        if (n++ >= 8) break;
                        Recent.Add(ParseSession(t as JObject));
                    }
                }
                catch { }
            }
            finally { Loading = false; }
        }

        private void ParseUsage(JObject u)
        {
            if (u == null) return;
            Sessions = u["sessions"]?.Value<int>() ?? 0;
            Messages = u["messages"]?.Value<int>() ?? 0;
            Tokens = u["tokens"]?.Value<long>() ?? u["total_tokens"]?.Value<long>() ?? 0;
            ActiveDays = u["active_days"]?.Value<int>() ?? 0;
            PeakHour = u["peak_hour"]?.Value<int>() ?? -1;
            OnPropertyChanged(nameof(PeakHourLabel));
            TopModel = u["top_model"]?.ToString() ?? u["model"]?.ToString() ?? "—";
            CostEstimate = u["cost_estimate"]?.ToString() ?? u["estimated_cost"]?.ToString() ?? "—";
            ActivityBlocks.Clear();
            var blocks = u["activity"] as JArray ?? u["weeks"] as JArray ?? u["activity_blocks"] as JArray;
            if (blocks != null)
                foreach (var b in blocks)
                {
                    int v = 0;
                    if (b.Type == JTokenType.Integer) v = b.Value<int>();
                    else v = b["count"]?.Value<int>() ?? b["messages"]?.Value<int>() ?? 0;
                    ActivityBlocks.Add(Math.Max(0, Math.Min(4, v > 20 ? 4 : v > 10 ? 3 : v > 3 ? 2 : v > 0 ? 1 : 0)));
                }
        }

        internal static ChatSession ParseSession(JObject t)
        {
            if (t == null) return new ChatSession();
            DateTime updated = DateTime.MinValue;
            var ts = t["updated_at"]?.ToString() ?? t["updatedAt"]?.ToString();
            DateTime.TryParse(ts, out updated);
            return new ChatSession
            {
                Id = t["id"]?.ToString() ?? t["session_id"]?.ToString() ?? "",
                Title = t["title"]?.ToString() ?? "Untitled",
                Profile = t["profile"]?.ToString() ?? "",
                Project = t["project"]?.ToString() ?? "",
                UpdatedAt = updated,
                Preview = t["preview"]?.ToString() ?? t["last_message"]?.ToString() ?? ""
            };
        }

        internal static IEnumerable<JToken> AsArray(JToken tok)
        {
            if (tok is JArray) return (JArray)tok;
            if (tok is JObject)
            {
                var o = (JObject)tok;
                foreach (var name in new[] { "sessions", "items", "profiles", "data", "results" })
                    if (o[name] is JArray) return (JArray)o[name];
            }
            return Enumerable.Empty<JToken>();
        }
    }
}
