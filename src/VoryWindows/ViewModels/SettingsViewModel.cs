using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json.Linq;
using VoryWindows.Auth;
using VoryWindows.Models;
using VoryWindows.Mvvm;
using VoryWindows.Services;
using VoryWindows.Views.Dialogs;

namespace VoryWindows.ViewModels
{
    public class SettingsSection
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public string Glyph { get; set; } = "";
    }

    /// <summary>Settings root: section navigation + one VM per section.</summary>
    public class SettingsViewModel : ViewModelBase
    {
        private readonly AppState _state;

        public ObservableCollection<SettingsSection> Sections { get; } =
            new ObservableCollection<SettingsSection>();

        public SettingsViewModel(AppState state)
        {
            _state = state;
            foreach (var s in new[]
            {
                new SettingsSection { Key = "gateways", Title = "Gateways", Glyph = "🌐" },
                new SettingsSection { Key = "model", Title = "Model", Glyph = "🧠" },
                new SettingsSection { Key = "config", Title = "Config", Glyph = "🔧" },
                new SettingsSection { Key = "env", Title = "Env / API keys", Glyph = "🔑" },
                new SettingsSection { Key = "tools", Title = "Tools", Glyph = "🛠" },
                new SettingsSection { Key = "skills", Title = "Skills", Glyph = "✨" },
                new SettingsSection { Key = "mcp", Title = "MCP", Glyph = "🔌" },
                new SettingsSection { Key = "approvals", Title = "Approvals", Glyph = "✅" },
                new SettingsSection { Key = "cron", Title = "Cron", Glyph = "⏰" },
                new SettingsSection { Key = "sessions", Title = "Sessions", Glyph = "💬" },
                new SettingsSection { Key = "channels", Title = "Channels", Glyph = "📣" },
                new SettingsSection { Key = "system", Title = "System", Glyph = "🖥" },
                new SettingsSection { Key = "maintenance", Title = "Maintenance", Glyph = "🧰" },
                new SettingsSection { Key = "plugins", Title = "Plugins", Glyph = "🧩" },
                new SettingsSection { Key = "appearance", Title = "Appearance", Glyph = "🎨" },
            }) Sections.Add(s);

            Gateways = new GatewaysSettingsVm(state, this);
            Model = new ModelSettingsVm(state);
            Config = new ConfigSettingsVm(state);
            Env = new EnvSettingsVm(state);
            Tools = new ToolsSettingsVm(state);
            Skills = new SkillsSettingsVm(state);
            Mcp = new McpSettingsVm(state);
            Approvals = new ApprovalsSettingsVm(state);
            Cron = new CronSettingsVm(state);
            Sessions = new SessionsSettingsVm(state);
            Channels = new ChannelsSettingsVm(state);
            System = new SystemSettingsVm(state);
            Maintenance = new MaintenanceSettingsVm(state);
            Plugins = new PluginsSettingsVm(state);
            Appearance = new AppearanceSettingsVm(state);

            SelectSectionCommand = new RelayCommand(p =>
            {
                var s = p as SettingsSection;
                if (s != null) SelectedSection = s;
            });
            SelectedSection = Sections[0];
        }

        private SettingsSection _selectedSection;
        public SettingsSection SelectedSection
        {
            get { return _selectedSection; }
            set
            {
                if (Set(ref _selectedSection, value) && value != null)
                {
                    SectionVmFor(value.Key)?.RefreshAsync();
                    OnPropertyChanged(nameof(CurrentSectionVm));
                }
            }
        }

        public ViewModelBase CurrentSectionVm
        {
            get { return _selectedSection == null ? null : SectionVmFor(_selectedSection.Key); }
        }

        public RelayCommand SelectSectionCommand { get; }

        public GatewaysSettingsVm Gateways { get; }
        public ModelSettingsVm Model { get; }
        public ConfigSettingsVm Config { get; }
        public EnvSettingsVm Env { get; }
        public ToolsSettingsVm Tools { get; }
        public SkillsSettingsVm Skills { get; }
        public McpSettingsVm Mcp { get; }
        public ApprovalsSettingsVm Approvals { get; }
        public CronSettingsVm Cron { get; }
        public SessionsSettingsVm Sessions { get; }
        public ChannelsSettingsVm Channels { get; }
        public SystemSettingsVm System { get; }
        public MaintenanceSettingsVm Maintenance { get; }
        public PluginsSettingsVm Plugins { get; }
        public AppearanceSettingsVm Appearance { get; }

        public ViewModelBase SectionVmFor(string key)
        {
            switch (key)
            {
                case "gateways": return Gateways;
                case "model": return Model;
                case "config": return Config;
                case "env": return Env;
                case "tools": return Tools;
                case "skills": return Skills;
                case "mcp": return Mcp;
                case "approvals": return Approvals;
                case "cron": return Cron;
                case "sessions": return Sessions;
                case "channels": return Channels;
                case "system": return System;
                case "maintenance": return Maintenance;
                case "plugins": return Plugins;
                case "appearance": return Appearance;
                default: return null;
            }
        }
    }

    public abstract class SectionVm : ViewModelBase
    {
        protected readonly AppState State;
        protected SectionVm(AppState state) { State = state; }

        private bool _loading;
        public bool Loading
        {
            get { return _loading; }
            set { Set(ref _loading, value); }
        }

        private string _error = "";
        public string Error
        {
            get { return _error; }
            set { Set(ref _error, value); }
        }

        public virtual async void RefreshAsync() { await Task.FromResult(0); }
    }

    // ---------- Gateways ----------

    public class GatewaysSettingsVm : SectionVm
    {
        private readonly SettingsViewModel _root;
        public GatewaysSettingsVm(AppState state, SettingsViewModel root) : base(state)
        {
            _root = root;
            AddCommand = new RelayCommand(() => EditGateway(null));
            EditCommand = new RelayCommand(p => EditGateway(p as GatewayCredential));
            DeleteCommand = new RelayCommand(p => Delete(p as GatewayCredential));
            ActivateCommand = new AsyncRelayCommand(p => ActivateAsync(p as GatewayCredential));
            ReconnectCommand = new AsyncRelayCommand(() => ReconnectAsync());
        }

        public RelayCommand AddCommand { get; }
        public RelayCommand EditCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public AsyncRelayCommand ActivateCommand { get; }
        public AsyncRelayCommand ReconnectCommand { get; }

        public System.Collections.ObjectModel.ObservableCollection<GatewayCredential> Gateways
        {
            get { return State.Gateways; }
        }

        private string _status = "";
        public string Status
        {
            get { return _status; }
            set { Set(ref _status, value); }
        }

        public override async void RefreshAsync()
        {
            Status = "Connection: " + State.ConnectionState
                + (State.ActiveGateway != null ? " — " + State.ActiveGateway.Name : "");
            await Task.FromResult(0);
        }

        private void EditGateway(GatewayCredential g)
        {
            var dlg = new GatewayDialog(State, g);
            dlg.Owner = App.Current.MainWindow;
            dlg.ShowDialog();
            RefreshAsync();
        }

        private void Delete(GatewayCredential g)
        {
            if (g == null) return;
            State.Gateways.Remove(g);
            State.SaveGateways();
            if (State.ActiveGateway == g)
                State.ActiveGateway = State.Gateways.FirstOrDefault();
            RefreshAsync();
        }

        private async Task ActivateAsync(GatewayCredential g)
        {
            if (g == null) return;
            await State.DisconnectAsync();
            State.ActiveGateway = g;
            await State.ConnectAsync();
            RefreshAsync();
        }

        private async Task ReconnectAsync()
        {
            await State.DisconnectAsync();
            await State.ConnectAsync();
            await State.ProbeRestartRequiredAsync();
            RefreshAsync();
        }
    }

    // ---------- Model ----------

    public class ModelSettingsVm : SectionVm
    {
        public ModelSettingsVm(AppState state) : base(state)
        {
            SaveCommand = new AsyncRelayCommand(() => SaveAsync());
        }

        public ObservableCollection<ModelOption> Options { get; } = new ObservableCollection<ModelOption>();

        private string _selectedModel = "";
        public string SelectedModel
        {
            get { return _selectedModel; }
            set { Set(ref _selectedModel, value); }
        }

        private string _auxiliary = "";
        public string Auxiliary
        {
            get { return _auxiliary; }
            set { Set(ref _auxiliary, value); }
        }

        public AsyncRelayCommand SaveCommand { get; }

        public override async void RefreshAsync()
        {
            if (State.Rest == null) return;
            Loading = true; Error = "";
            try
            {
                var tok = await State.Rest.GetAsync("/api/model/options");
                RunOnUi(() =>
                {
                    Options.Clear();
                    foreach (var t in HomeViewModel.AsArray(tok is JObject ? ((JObject)tok)["models"] ?? tok : tok))
                    {
                        var o = t as JObject;
                        Options.Add(new ModelOption
                        {
                            Label = o?["id"]?.ToString() ?? o?["name"]?.ToString() ?? t.ToString(),
                            Provider = o?["provider"]?.ToString() ?? "",
                            Slug = o?["slug"]?.ToString() ?? ""
                        });
                    }
                });
                try
                {
                    var aux = await State.Rest.GetAsync("/api/model/auxiliary");
                    Auxiliary = (aux as JObject)?["model"]?.ToString() ?? aux.ToString();
                }
                catch { }
            }
            catch (Exception ex) { Error = ex.Message; }
            finally { Loading = false; }
        }

        private async Task SaveAsync()
        {
            if (State.Rest == null || string.IsNullOrWhiteSpace(SelectedModel)) return;
            try
            {
                // Global default change goes through the dashboard API (never model.default writes).
                await State.Rest.PostJsonAsync("/api/model/set",
                    new JObject { ["model"] = SelectedModel });
                Error = "";
            }
            catch (Exception ex) { Error = ex.Message; }
        }
    }

    // ---------- Config ----------

    public class ConfigEntry
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public class ConfigSettingsVm : SectionVm
    {
        public ConfigSettingsVm(AppState state) : base(state)
        {
            SaveCommand = new AsyncRelayCommand(() => SaveAsync());
        }

        public ObservableCollection<ConfigEntry> Entries { get; } = new ObservableCollection<ConfigEntry>();
        public AsyncRelayCommand SaveCommand { get; }

        public override async void RefreshAsync()
        {
            if (State.Rest == null) return;
            Loading = true; Error = "";
            try
            {
                var tok = await State.Rest.GetAsync("/api/config");
                var cfg = (tok as JObject)?["config"] as JObject ?? tok as JObject;
                RunOnUi(() =>
                {
                    Entries.Clear();
                    if (cfg != null)
                        foreach (var p in cfg.Properties())
                        {
                            if (p.Name == "model.default") continue; // never write model.default
                            Entries.Add(new ConfigEntry { Key = p.Name, Value = p.Value.ToString() });
                        }
                });
            }
            catch (Exception ex) { Error = ex.Message; }
            finally { Loading = false; }
        }

        private async Task SaveAsync()
        {
            if (State.Rest == null) return;
            try
            {
                var cfg = new JObject();
                foreach (var e in Entries)
                {
                    if (e.Key == "model.default") continue;
                    JToken v;
                    try { v = JToken.Parse(e.Value); }
                    catch { v = new JValue(e.Value); }
                    cfg[e.Key] = v;
                }
                await State.Rest.PutJsonAsync("/api/config", new JObject { ["config"] = cfg });
                Error = "";
                RefreshAsync();
            }
            catch (Exception ex) { Error = ex.Message; }
        }
    }

    // ---------- Env ----------

    public class EnvEntry
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
        public bool IsNew { get; set; }
    }

    public class EnvSettingsVm : SectionVm
    {
        public EnvSettingsVm(AppState state) : base(state)
        {
            SaveCommand = new AsyncRelayCommand(p => SaveAsync(p as EnvEntry));
            DeleteCommand = new AsyncRelayCommand(p => DeleteAsync(p as EnvEntry));
            AddCommand = new RelayCommand(() =>
                RunOnUi(() => Entries.Add(new EnvEntry { IsNew = true })));
        }

        public ObservableCollection<EnvEntry> Entries { get; } = new ObservableCollection<EnvEntry>();
        public AsyncRelayCommand SaveCommand { get; }
        public AsyncRelayCommand DeleteCommand { get; }
        public RelayCommand AddCommand { get; }

        public override async void RefreshAsync()
        {
            if (State.Rest == null) return;
            Loading = true; Error = "";
            try
            {
                var tok = await State.Rest.GetAsync("/api/env");
                var o = tok as JObject;
                RunOnUi(() =>
                {
                    Entries.Clear();
                    if (o != null)
                        foreach (var p in o.Properties())
                            Entries.Add(new EnvEntry { Key = p.Name, Value = p.Value.ToString() });
                });
            }
            catch (Exception ex) { Error = ex.Message; }
            finally { Loading = false; }
        }

        private async Task SaveAsync(EnvEntry e)
        {
            if (e == null || State.Rest == null || string.IsNullOrWhiteSpace(e.Key)) return;
            try
            {
                await State.Rest.PutJsonAsync("/api/env",
                    new JObject { ["key"] = e.Key, ["value"] = e.Value ?? "" });
                e.IsNew = false;
                Error = "";
            }
            catch (Exception ex) { Error = ex.Message; }
        }

        private async Task DeleteAsync(EnvEntry e)
        {
            if (e == null || State.Rest == null) return;
            try
            {
                await State.Rest.DeleteJsonAsync("/api/env", new JObject { ["key"] = e.Key });
                RunOnUi(() => Entries.Remove(e));
            }
            catch (Exception ex) { Error = ex.Message; }
        }
    }

    // ---------- Tools / Skills / MCP ----------

    public class NamedToggle
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public bool Enabled { get; set; }
    }

    public class ToolsSettingsVm : SectionVm
    {
        public ToolsSettingsVm(AppState state) : base(state)
        {
            ToggleCommand = new AsyncRelayCommand(p => ToggleAsync(p as NamedToggle));
        }

        public ObservableCollection<NamedToggle> Toolsets { get; } = new ObservableCollection<NamedToggle>();
        public AsyncRelayCommand ToggleCommand { get; }

        public override async void RefreshAsync()
        {
            if (State.Rest == null) return;
            Loading = true; Error = "";
            try
            {
                var tok = await State.Rest.GetAsync("/api/tools/toolsets");
                RunOnUi(() =>
                {
                    Toolsets.Clear();
                    foreach (var t in HomeViewModel.AsArray(tok))
                    {
                        var o = t as JObject;
                        Toolsets.Add(new NamedToggle
                        {
                            Name = o?["name"]?.ToString() ?? t.ToString(),
                            Description = o?["description"]?.ToString() ?? "",
                            Enabled = o?["enabled"]?.Value<bool>() ?? false
                        });
                    }
                });
            }
            catch (Exception ex) { Error = ex.Message; }
            finally { Loading = false; }
        }

        private async Task ToggleAsync(NamedToggle t)
        {
            if (t == null || State.Rest == null) return;
            try
            {
                await State.Rest.PutJsonAsync("/api/tools/toolsets/" + Uri.EscapeDataString(t.Name),
                    new JObject { ["enabled"] = t.Enabled });
            }
            catch (Exception ex) { Error = ex.Message; RefreshAsync(); }
        }
    }

    public class SkillsSettingsVm : SectionVm
    {
        public SkillsSettingsVm(AppState state) : base(state)
        {
            ToggleCommand = new AsyncRelayCommand(p => ToggleAsync(p as NamedToggle));
        }

        public ObservableCollection<NamedToggle> Skills { get; } = new ObservableCollection<NamedToggle>();
        public AsyncRelayCommand ToggleCommand { get; }

        public override async void RefreshAsync()
        {
            if (State.Rest == null) return;
            Loading = true; Error = "";
            try
            {
                var tok = await State.Rest.GetAsync("/api/skills");
                RunOnUi(() =>
                {
                    Skills.Clear();
                    foreach (var t in HomeViewModel.AsArray(tok))
                    {
                        var o = t as JObject;
                        Skills.Add(new NamedToggle
                        {
                            Name = o?["name"]?.ToString() ?? t.ToString(),
                            Description = o?["description"]?.ToString() ?? "",
                            Enabled = o?["enabled"]?.Value<bool>() ?? false
                        });
                    }
                });
            }
            catch (Exception ex) { Error = ex.Message; }
            finally { Loading = false; }
        }

        private async Task ToggleAsync(NamedToggle s)
        {
            if (s == null || State.Rest == null) return;
            try
            {
                await State.Rest.PutJsonAsync("/api/skills/toggle",
                    new JObject { ["name"] = s.Name, ["enabled"] = s.Enabled });
            }
            catch (Exception ex) { Error = ex.Message; RefreshAsync(); }
        }
    }

    public class McpServer
    {
        public string Name { get; set; } = "";
        public bool Enabled { get; set; }
        public string Status { get; set; } = "";
    }

    public class McpSettingsVm : SectionVm
    {
        public McpSettingsVm(AppState state) : base(state)
        {
            ToggleCommand = new AsyncRelayCommand(p => ToggleAsync(p as McpServer));
            TestCommand = new AsyncRelayCommand(p => TestAsync(p as McpServer));
            DeleteCommand = new AsyncRelayCommand(p => DeleteAsync(p as McpServer));
        }

        public ObservableCollection<McpServer> Servers { get; } = new ObservableCollection<McpServer>();
        public AsyncRelayCommand ToggleCommand { get; }
        public AsyncRelayCommand TestCommand { get; }
        public AsyncRelayCommand DeleteCommand { get; }

        public override async void RefreshAsync()
        {
            if (State.Rest == null) return;
            Loading = true; Error = "";
            try
            {
                var tok = await State.Rest.GetAsync("/api/mcp/servers");
                RunOnUi(() =>
                {
                    Servers.Clear();
                    foreach (var t in HomeViewModel.AsArray(tok))
                    {
                        var o = t as JObject;
                        Servers.Add(new McpServer
                        {
                            Name = o?["name"]?.ToString() ?? t.ToString(),
                            Enabled = o?["enabled"]?.Value<bool>() ?? false,
                            Status = o?["status"]?.ToString() ?? ""
                        });
                    }
                });
            }
            catch (Exception ex) { Error = ex.Message; }
            finally { Loading = false; }
        }

        private async Task ToggleAsync(McpServer s)
        {
            if (s == null || State.Rest == null) return;
            try
            {
                await State.Rest.PutJsonAsync("/api/mcp/servers/" + Uri.EscapeDataString(s.Name) + "/enabled",
                    new JObject { ["enabled"] = s.Enabled });
            }
            catch (Exception ex) { Error = ex.Message; RefreshAsync(); }
        }

        private async Task TestAsync(McpServer s)
        {
            if (s == null || State.Rest == null) return;
            try
            {
                var r = await State.Rest.PostJsonAsync(
                    "/api/mcp/servers/" + Uri.EscapeDataString(s.Name) + "/test", new JObject());
                Error = "";
                s.Status = (r as JObject)?["status"]?.ToString() ?? "ok";
            }
            catch (Exception ex) { Error = ex.Message; }
        }

        private async Task DeleteAsync(McpServer s)
        {
            if (s == null || State.Rest == null) return;
            try
            {
                await State.Rest.DeleteJsonAsync("/api/mcp/servers/" + Uri.EscapeDataString(s.Name));
                RefreshAsync();
            }
            catch (Exception ex) { Error = ex.Message; }
        }
    }

    // ---------- Approvals ----------

    public class ApprovalsSettingsVm : SectionVm
    {
        public ApprovalsSettingsVm(AppState state) : base(state)
        {
            SaveCommand = new AsyncRelayCommand(() => SaveAsync());
        }

        private string _mode = "prompt";
        public string Mode
        {
            get { return _mode; }
            set { Set(ref _mode, value); }
        }

        private int _timeout = 120;
        public int Timeout
        {
            get { return _timeout; }
            set { Set(ref _timeout, value); }
        }

        public List<string> Modes { get; } = new List<string> { "prompt", "auto", "deny", "yolo" };
        public AsyncRelayCommand SaveCommand { get; }

        public override async void RefreshAsync()
        {
            if (State.Rest == null) return;
            try
            {
                var tok = await State.Rest.GetAsync("/api/config");
                var cfg = (tok as JObject)?["config"] as JObject;
                var ap = cfg?["approvals"] as JObject;
                if (ap != null)
                {
                    Mode = ap["mode"]?.ToString() ?? Mode;
                    Timeout = ap["timeout"]?.Value<int>() ?? Timeout;
                }
            }
            catch (Exception ex) { Error = ex.Message; }
        }

        private async Task SaveAsync()
        {
            if (State.Rest == null) return;
            try
            {
                await State.Rest.PutJsonAsync("/api/config", new JObject
                {
                    ["config"] = new JObject
                    {
                        ["approvals"] = new JObject { ["mode"] = Mode, ["timeout"] = Timeout }
                    }
                });
                Error = "";
            }
            catch (Exception ex) { Error = ex.Message; }
        }
    }

    // ---------- Cron ----------

    public class CronSettingsVm : SectionVm
    {
        public CronSettingsVm(AppState state) : base(state)
        {
            PauseCommand = new AsyncRelayCommand(p => ActAsync(p as CronJob, "pause"));
            ResumeCommand = new AsyncRelayCommand(p => ActAsync(p as CronJob, "resume"));
            TriggerCommand = new AsyncRelayCommand(p => ActAsync(p as CronJob, "trigger"));
            DeleteCommand = new AsyncRelayCommand(p => DeleteAsync(p as CronJob));
            AddCommand = new RelayCommand(() => AddJob());
        }

        public ObservableCollection<CronJob> Jobs { get; } = new ObservableCollection<CronJob>();
        public AsyncRelayCommand PauseCommand { get; }
        public AsyncRelayCommand ResumeCommand { get; }
        public AsyncRelayCommand TriggerCommand { get; }
        public AsyncRelayCommand DeleteCommand { get; }
        public RelayCommand AddCommand { get; }

        public override async void RefreshAsync()
        {
            if (State.Rest == null) return;
            Loading = true; Error = "";
            try
            {
                var tok = await State.Rest.GetAsync("/api/cron/jobs");
                RunOnUi(() =>
                {
                    Jobs.Clear();
                    foreach (var t in HomeViewModel.AsArray(tok))
                    {
                        var o = t as JObject;
                        Jobs.Add(new CronJob
                        {
                            Id = o?["id"]?.ToString() ?? "",
                            Name = o?["name"]?.ToString() ?? "",
                            Schedule = o?["schedule"]?.ToString() ?? o?["cron"]?.ToString() ?? "",
                            Paused = o?["paused"]?.Value<bool>() ?? false,
                            LastRun = o?["last_run"]?.ToString() ?? ""
                        });
                    }
                });
            }
            catch (Exception ex) { Error = ex.Message; }
            finally { Loading = false; }
        }

        private async Task ActAsync(CronJob j, string action)
        {
            if (j == null || State.Rest == null) return;
            try
            {
                await State.Rest.PostJsonAsync("/api/cron/jobs/" + Uri.EscapeDataString(j.Id) + "/" + action,
                    new JObject());
                RefreshAsync();
            }
            catch (Exception ex) { Error = ex.Message; }
        }

        private async Task DeleteAsync(CronJob j)
        {
            if (j == null || State.Rest == null) return;
            try
            {
                await State.Rest.DeleteJsonAsync("/api/cron/jobs/" + Uri.EscapeDataString(j.Id));
                RefreshAsync();
            }
            catch (Exception ex) { Error = ex.Message; }
        }

        private void AddJob()
        {
            var dlg = new CronJobDialog();
            dlg.Owner = App.Current.MainWindow;
            if (dlg.ShowDialog() != true) return;
            AddJobAsync(dlg.JobName, dlg.JobSchedule, dlg.JobCommand);
        }

        private async void AddJobAsync(string name, string schedule, string command)
        {
            try
            {
                await State.Rest.PostJsonAsync("/api/cron/jobs",
                    new JObject { ["name"] = name, ["schedule"] = schedule, ["command"] = command });
                RefreshAsync();
            }
            catch (Exception ex) { Error = ex.Message; }
        }
    }

    // ---------- Sessions ----------

    public class SessionsSettingsVm : SectionVm
    {
        public SessionsSettingsVm(AppState state) : base(state)
        {
            SearchCommand = new AsyncRelayCommand(() => RefreshAsync());
            DeleteCommand = new AsyncRelayCommand(p => DeleteAsync(p as ChatSession));
        }

        public ObservableCollection<ChatSession> Sessions { get; } = new ObservableCollection<ChatSession>();

        private string _query = "";
        public string Query
        {
            get { return _query; }
            set { Set(ref _query, value); }
        }

        private string _stats = "";
        public string Stats
        {
            get { return _stats; }
            set { Set(ref _stats, value); }
        }

        public AsyncRelayCommand SearchCommand { get; }
        public AsyncRelayCommand DeleteCommand { get; }

        public override async void RefreshAsync()
        {
            if (State.Rest == null) return;
            Loading = true; Error = "";
            try
            {
                JToken tok;
                if (string.IsNullOrWhiteSpace(Query))
                    tok = await State.Rest.GetAsync("/api/sessions",
                        new Dictionary<string, string> { { "order", "recent" } });
                else
                    tok = await State.Rest.GetAsync("/api/sessions/search",
                        new Dictionary<string, string> { { "q", Query } });
                RunOnUi(() =>
                {
                    Sessions.Clear();
                    foreach (var t in HomeViewModel.AsArray(tok))
                        Sessions.Add(HomeViewModel.ParseSession(t as JObject));
                });
                try
                {
                    var st = await State.Rest.GetAsync("/api/sessions/stats");
                    Stats = st.ToString(Newtonsoft.Json.Formatting.None);
                }
                catch { }
            }
            catch (Exception ex) { Error = ex.Message; }
            finally { Loading = false; }
        }

        private async Task DeleteAsync(ChatSession s)
        {
            if (s == null || State.Rest == null) return;
            try
            {
                await State.Rest.DeleteJsonAsync("/api/sessions/" + Uri.EscapeDataString(s.Id));
                RunOnUi(() => Sessions.Remove(s));
            }
            catch (Exception ex) { Error = ex.Message; }
        }
    }

    // ---------- Channels (read-only) / Plugins (read-only) ----------

    public class ChannelsSettingsVm : SectionVm
    {
        public ChannelsSettingsVm(AppState state) : base(state) { }

        private string _platforms = "";
        public string Platforms
        {
            get { return _platforms; }
            set { Set(ref _platforms, value); }
        }

        public override async void RefreshAsync()
        {
            if (State.Rest == null) return;
            try
            {
                var tok = await State.Rest.GetAsync("/api/messaging/platforms");
                Platforms = tok.ToString(Newtonsoft.Json.Formatting.Indented);
            }
            catch (Exception ex) { Error = ex.Message; }
        }
    }

    public class PluginsSettingsVm : SectionVm
    {
        public PluginsSettingsVm(AppState state) : base(state) { }

        public ObservableCollection<string> Plugins { get; } = new ObservableCollection<string>();

        public override async void RefreshAsync()
        {
            if (State.Rest == null) return;
            Loading = true; Error = "";
            try
            {
                var tok = await State.Rest.GetAsync("/api/dashboard/plugins/hub");
                RunOnUi(() =>
                {
                    Plugins.Clear();
                    foreach (var t in HomeViewModel.AsArray(tok))
                        Plugins.Add((t as JObject)?["name"]?.ToString() ?? t.ToString());
                });
            }
            catch (Exception ex) { Error = ex.Message; }
            finally { Loading = false; }
        }
    }

    // ---------- System ----------

    public class SystemSettingsVm : SectionVm
    {
        public SystemSettingsVm(AppState state) : base(state)
        {
            DoctorCommand = new AsyncRelayCommand(() => DoctorAsync());
        }

        private string _status = "";
        public string StatusText
        {
            get { return _status; }
            set { Set(ref _status, value); }
        }

        private string _logs = "";
        public string Logs
        {
            get { return _logs; }
            set { Set(ref _logs, value); }
        }

        private string _doctor = "";
        public string DoctorResult
        {
            get { return _doctor; }
            set { Set(ref _doctor, value); }
        }

        public AsyncRelayCommand DoctorCommand { get; }

        public override async void RefreshAsync()
        {
            if (State.Rest == null) return;
            Loading = true; Error = "";
            try
            {
                var st = await State.Rest.GetAsync("/api/status");
                StatusText = st.ToString(Newtonsoft.Json.Formatting.Indented);
                try
                {
                    var logs = await State.Rest.GetAsync("/api/logs");
                    Logs = logs.ToString(Newtonsoft.Json.Formatting.Indented);
                    if (Logs.Length > 6000) Logs = Logs.Substring(0, 6000) + "…";
                }
                catch (Exception ex) { Logs = "Logs unavailable: " + ex.Message; }
                Logs += "\n\n— Local app log (" + Log.LogPath + ") —\n" + Log.Tail(4000);
            }
            catch (Exception ex) { Error = ex.Message; }
            finally { Loading = false; }
        }

        private async Task DoctorAsync()
        {
            if (State.Rest == null) return;
            try
            {
                var r = await State.Rest.PostJsonAsync("/api/ops/doctor", new JObject());
                DoctorResult = r.ToString(Newtonsoft.Json.Formatting.Indented);
            }
            catch (Exception ex) { Error = ex.Message; }
        }
    }

    // ---------- Maintenance ----------

    public class MaintenanceSettingsVm : SectionVm
    {
        public MaintenanceSettingsVm(AppState state) : base(state)
        {
            CheckCommand = new AsyncRelayCommand(() => CheckAsync());
            UpdateCommand = new AsyncRelayCommand(() => UpdateAsync());
            RestartCommand = new AsyncRelayCommand(() => RestartAsync());
            TailCommand = new AsyncRelayCommand(p => TailAsync(p as string));
        }

        private string _check = "";
        public string CheckResult
        {
            get { return _check; }
            set { Set(ref _check, value); }
        }

        private string _actionStatus = "";
        public string ActionStatus
        {
            get { return _actionStatus; }
            set { Set(ref _actionStatus, value); }
        }

        public AsyncRelayCommand CheckCommand { get; }
        public AsyncRelayCommand UpdateCommand { get; }
        public AsyncRelayCommand RestartCommand { get; }
        public AsyncRelayCommand TailCommand { get; }

        private async Task CheckAsync()
        {
            if (State.Rest == null) return;
            try { CheckResult = (await State.Rest.GetAsync("/api/hermes/update/check")).ToString(); }
            catch (Exception ex) { Error = ex.Message; }
        }

        private async Task UpdateAsync()
        {
            if (State.Rest == null) return;
            try
            {
                await State.Rest.PostJsonAsync("/api/hermes/update", new JObject());
                await TailAsync("hermes-update");
                await State.ProbeRestartRequiredAsync();
            }
            catch (Exception ex) { Error = ex.Message; }
        }

        private async Task RestartAsync()
        {
            if (State.Rest == null) return;
            try
            {
                await State.Rest.PostJsonAsync("/api/gateway/restart", new JObject());
                await TailAsync("gateway-restart");
                // Reconnect after the gateway comes back.
                await Task.Delay(5000);
                await State.DisconnectAsync();
                await State.ConnectAsync();
            }
            catch (Exception ex) { Error = ex.Message; }
        }

        private async Task TailAsync(string action)
        {
            if (State.Rest == null || string.IsNullOrEmpty(action)) return;
            try
            {
                var r = await State.Rest.GetAsync("/api/actions/" + action + "/status");
                ActionStatus = r.ToString(Newtonsoft.Json.Formatting.Indented);
            }
            catch (Exception ex) { Error = ex.Message; }
        }
    }

    // ---------- Appearance ----------

    public class AppearanceSettingsVm : SectionVm
    {
        public AppearanceSettingsVm(AppState state) : base(state)
        {
            ApplyCommand = new RelayCommand(() => Apply());
        }

        public List<string> Accents { get; } = new List<string>
        {
            "#5B8DEF", "#8E6BEF", "#46A758", "#E8A13C", "#E5484D", "#12A594"
        };

        private string _accent;
        public string Accent
        {
            get { return _accent; }
            set { Set(ref _accent, value); }
        }

        private bool _darkTheme;
        public bool DarkTheme
        {
            get { return _darkTheme; }
            set { Set(ref _darkTheme, value); }
        }

        private string _userName;
        public string UserName
        {
            get { return _userName; }
            set { Set(ref _userName, value); }
        }

        public RelayCommand ApplyCommand { get; }

        public override async void RefreshAsync()
        {
            Accent = State.Accent;
            DarkTheme = State.DarkTheme;
            UserName = State.UserName;
            await Task.FromResult(0);
        }

        private void Apply()
        {
            State.Accent = Accent ?? State.Accent;
            State.DarkTheme = DarkTheme;
            State.UserName = UserName ?? "";
            App.Current.Resources.MergedDictionaries.Clear();
            App.Current.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
            {
                Source = new Uri("Resources/" + (DarkTheme ? "DarkTheme" : "LightTheme") + ".xaml",
                    UriKind.Relative)
            });
            // Apply accent
            if (App.Current.Resources["AccentBrush"] is System.Windows.Media.SolidColorBrush)
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(State.Accent);
                ((System.Windows.Media.SolidColorBrush)App.Current.Resources["AccentBrush"]).Color = c;
            }
        }
    }
}
