using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using VoryWindows.Models;
using VoryWindows.Mvvm;
using VoryWindows.Networking;
using VoryWindows.Services;

namespace VoryWindows.ViewModels
{
    public class AttachedFile
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public long Size { get; set; }
    }

    public class ModelGroup
    {
        public string Provider { get; set; } = "";
        public string Slug { get; set; } = "";
        public List<ModelOption> Models { get; set; } = new List<ModelOption>();
    }

    public class ChatThreadViewModel : ViewModelBase
    {
        private readonly AppState _state;
        private readonly MainViewModel _main;

        public ObservableCollection<ChatItem> Items { get; } = new ObservableCollection<ChatItem>();
        public ObservableCollection<AttachedFile> Attachments { get; } = new ObservableCollection<AttachedFile>();
        public ObservableCollection<string> Suggestions { get; } = new ObservableCollection<string>();
        public ObservableCollection<ModelGroup> ModelGroups { get; } = new ObservableCollection<ModelGroup>();

        private static readonly string[] LocalSlash = new[]
        {
            "/approve", "/deny", "/stop", "/new", "/title", "/model", "/reasoning"
        };
        private List<string> _catalogCommands = new List<string>();
        private bool _catalogLoaded;

        public ChatThreadViewModel(AppState state, MainViewModel main)
        {
            _state = state;
            _main = main;
            state.GatewayEvent += OnGatewayEvent;
            state.ApprovalRequested += OnApproval;
            state.PromptRequested += OnPrompt;

            SendCommand = new AsyncRelayCommand(SendAsync, () => !string.IsNullOrWhiteSpace(ComposerText) || Attachments.Count > 0);
            StopCommand = new RelayCommand(() => StopTurn(), () => _streaming);
            AttachCommand = new RelayCommand(_ => RequestAttach());
            RemoveAttachmentCommand = new RelayCommand(p => { var a = p as AttachedFile; if (a != null) Attachments.Remove(a); });
            ApproveOnceCommand = new AsyncRelayCommand(p => AnswerApprovalAsync(p as ChatItem, "once"));
            ApproveSessionCommand = new AsyncRelayCommand(p => AnswerApprovalAsync(p as ChatItem, "session"));
            ApproveAlwaysCommand = new AsyncRelayCommand(p => AnswerApprovalAsync(p as ChatItem, "always"));
            DenyCommand = new AsyncRelayCommand(p => AnswerApprovalAsync(p as ChatItem, "deny"));
            AnswerPromptCommand = new AsyncRelayCommand(p => AnswerPromptAsync(p as ChatItem));
            SuggestionCommand = new RelayCommand(p => ApplySuggestion(p as string));
            PickModelCommand = new AsyncRelayCommand(p => PickModelAsync(p as ModelOption));
            ToggleModelPickerCommand = new RelayCommand(() => { ShowModelPicker = !ShowModelPicker; if (ShowModelPicker) LoadModelOptions(); });
            NewChatCommand = new AsyncRelayCommand(() => NewChatAsync());
            BackCommand = new RelayCommand(() => { /* desktop: keep thread, focus list */ });
        }

        public event Action AttachRequested;

        private string _sessionId = "";
        public string SessionId
        {
            get { return _sessionId; }
            set { Set(ref _sessionId, value); }
        }

        private string _title = "New chat";
        public string Title
        {
            get { return _title; }
            set { Set(ref _title, value); }
        }

        private string _botStatus = "";
        public string BotStatus
        {
            get { return _botStatus; }
            set { Set(ref _botStatus, value); }
        }

        private string _composerText = "";
        public string ComposerText
        {
            get { return _composerText; }
            set
            {
                if (Set(ref _composerText, value))
                {
                    SendCommand.RaiseCanExecuteChanged();
                    UpdateSuggestions();
                }
            }
        }

        private bool _showSuggestions;
        public bool ShowSuggestions
        {
            get { return _showSuggestions; }
            set { Set(ref _showSuggestions, value); }
        }

        private bool _showModelPicker;
        public bool ShowModelPicker
        {
            get { return _showModelPicker; }
            set { Set(ref _showModelPicker, value); }
        }

        private bool _streaming;
        public bool Streaming
        {
            get { return _streaming; }
            set { Set(ref _streaming, value); StopCommand.RaiseCanExecuteChanged(); }
        }

        private string _currentModel = "";
        public string CurrentModel
        {
            get { return _currentModel; }
            set { Set(ref _currentModel, value); }
        }

        public AsyncRelayCommand SendCommand { get; }
        public RelayCommand StopCommand { get; }
        public RelayCommand AttachCommand { get; }
        public RelayCommand RemoveAttachmentCommand { get; }
        public AsyncRelayCommand ApproveOnceCommand { get; }
        public AsyncRelayCommand ApproveSessionCommand { get; }
        public AsyncRelayCommand ApproveAlwaysCommand { get; }
        public AsyncRelayCommand DenyCommand { get; }
        public AsyncRelayCommand AnswerPromptCommand { get; }
        public RelayCommand SuggestionCommand { get; }
        public AsyncRelayCommand PickModelCommand { get; }
        public RelayCommand ToggleModelPickerCommand { get; }
        public AsyncRelayCommand NewChatCommand { get; }
        public RelayCommand BackCommand { get; }

        private void RequestAttach()
        {
            AttachRequested?.Invoke();
        }

        public void AddAttachment(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length > 25 * 1024 * 1024) return;
                RunOnUi(() => Attachments.Add(new AttachedFile
                {
                    Path = path, Name = fi.Name, Size = fi.Length
                }));
            }
            catch { }
        }

        // ---------- session lifecycle ----------

        public async void OpenSessionAsync(string sessionId, string profile)
        {
            SessionId = sessionId ?? "";
            RunOnUi(() => { Items.Clear(); Attachments.Clear(); });
            _main.Chats.ClearNeedsYou(SessionId);
            BotStatus = "Connecting…";
            try
            {
                var res = await _state.RpcAsync("session.resume", new JObject { ["session_id"] = SessionId });
                var msgs = res["messages"] as JArray;
                if (msgs == null || msgs.Count == 0)
                {
                    try
                    {
                        var rest = await _state.Rest.GetAsync("/api/sessions/" + Uri.EscapeDataString(SessionId) + "/messages");
                        msgs = (rest as JObject)?["messages"] as JArray ?? rest as JArray;
                    }
                    catch { }
                }
                RunOnUi(() =>
                {
                    Title = res["title"]?.ToString() ?? "Chat";
                    if (msgs != null)
                        foreach (var m in msgs)
                            Items.Add(MapHistoryMessage(m as JObject));
                    BotStatus = "";
                });
            }
            catch (Exception ex)
            {
                AddSystem("Could not open session: " + ex.Message);
                BotStatus = "";
            }
        }

        private ChatItem MapHistoryMessage(JObject m)
        {
            if (m == null) return new ChatItem { Kind = ChatItemKind.System, Text = "" };
            var role = (m["role"]?.ToString() ?? "").ToLowerInvariant();
            var text = ExtractText(m["content"] ?? m["text"]);
            DateTime t = DateTime.Now;
            DateTime.TryParse(m["timestamp"]?.ToString() ?? m["created_at"]?.ToString(), out t);
            if (role.Contains("user"))
                return new ChatItem { Kind = ChatItemKind.User, Text = text, Time = t };
            if (role.Contains("tool"))
                return new ChatItem
                {
                    Kind = ChatItemKind.Tool,
                    ToolName = m["tool"]?.ToString() ?? m["name"]?.ToString() ?? "tool",
                    ToolOutput = Cap(text, 4000),
                    Time = t
                };
            return new ChatItem { Kind = ChatItemKind.Assistant, Text = text, Time = t };
        }

        private static string ExtractText(JToken content)
        {
            if (content == null) return "";
            if (content.Type == JTokenType.String) return content.Value<string>() ?? "";
            if (content is JArray)
            {
                var parts = new List<string>();
                foreach (var b in (JArray)content)
                {
                    if (b.Type == JTokenType.String) parts.Add(b.Value<string>());
                    else if (b["text"] != null) parts.Add(b["text"].ToString());
                }
                return string.Join("", parts);
            }
            return content.ToString();
        }

        // ---------- sending ----------

        private async Task SendAsync(object _)
        {
            var text = (ComposerText ?? "").TrimEnd();
            if (string.IsNullOrWhiteSpace(text) && Attachments.Count == 0) return;
            if (string.IsNullOrEmpty(SessionId))
            {
                try
                {
                    var res = await _state.RpcAsync("session.create", new JObject());
                    SessionId = res["session_id"]?.ToString() ?? res["id"]?.ToString() ?? "";
                    await _main.Chats.RefreshAsync();
                }
                catch (Exception ex) { AddSystem("Could not create session: " + ex.Message); return; }
            }

            // Attachments first: upload, collect @file: refs.
            var refs = new List<string>();
            foreach (var a in Attachments.ToList())
            {
                var r = await AttachFileAsync(a);
                if (!string.IsNullOrEmpty(r)) refs.Add(r);
            }
            RunOnUi(() => Attachments.Clear());

            var fullText = text;
            if (refs.Count > 0) fullText = (fullText + " " + string.Join(" ", refs)).Trim();

            RunOnUi(() =>
            {
                Items.Add(new ChatItem { Kind = ChatItemKind.User, Text = fullText });
                ComposerText = "";
                ShowSuggestions = false;
            });

            // Local slash commands.
            if (fullText.StartsWith("/"))
            {
                if (await HandleLocalSlashAsync(fullText)) return;
            }

            await SubmitPromptAsync(fullText);
        }

        private async Task<bool> HandleLocalSlashAsync(string text)
        {
            var space = text.IndexOf(' ');
            var cmd = space < 0 ? text : text.Substring(0, space);
            var arg = space < 0 ? "" : text.Substring(space + 1).Trim();
            switch (cmd.ToLowerInvariant())
            {
                case "/approve":
                    await AnswerLatestApprovalAsync("once"); return true;
                case "/deny":
                    await AnswerLatestApprovalAsync("deny"); return true;
                case "/stop":
                    StopTurn();
                    await BestEffortRpcAsync("session.stop", new JObject { ["session_id"] = SessionId }, "Stop");
                    return true;
                case "/new":
                    await NewChatAsync(); return true;
                case "/title":
                    if (string.IsNullOrWhiteSpace(arg)) { AddSystem("Usage: /title <new title>"); return true; }
                    await BestEffortRpcAsync("session.set_title",
                        new JObject { ["session_id"] = SessionId, ["title"] = arg }, "Rename");
                    Title = arg;
                    return true;
                case "/model":
                    ShowModelPicker = true; LoadModelOptions(); return true;
                case "/reasoning":
                    await BestEffortRpcAsync("config.set",
                        new JObject { ["key"] = "reasoning_effort", ["value"] = arg, ["scope"] = "session" },
                        "Reasoning");
                    return true;
            }
            // Catalog-backed slash command.
            try
            {
                var name = cmd.TrimStart('/');
                try
                {
                    await _state.RpcAsync("slash.exec",
                        new JObject { ["name"] = name, ["args"] = arg, ["session_id"] = SessionId });
                }
                catch (InvalidOperationException)
                {
                    await _state.RpcAsync("command.dispatch",
                        new JObject { ["command"] = cmd, ["args"] = arg, ["session_id"] = SessionId });
                }
            }
            catch (Exception ex) { AddSystem("Slash command failed: " + ex.Message); }
            return true;
        }

        private async Task BestEffortRpcAsync(string method, JObject parameters, string label)
        {
            try { await _state.RpcAsync(method, parameters); AddSystem(label + " sent."); }
            catch (Exception ex) { AddSystem(label + " failed: " + ex.Message); }
        }

        private async Task NewChatAsync()
        {
            try
            {
                var res = await _state.RpcAsync("session.create", new JObject());
                var id = res["session_id"]?.ToString() ?? res["id"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(id))
                {
                    OpenSessionAsync(id, _state.ActiveGateway?.SelectedProfile ?? "");
                    await _main.Chats.RefreshAsync();
                }
            }
            catch (Exception ex) { AddSystem("Could not start a new chat: " + ex.Message); }
        }

        private async Task SubmitPromptAsync(string text)
        {
            _streamStart = DateTime.Now;
            _streamChars = 0;
            _turnInputTokens = -1;
            _turnOutputTokens = -1;
            Streaming = true;
            BotStatus = "Thinking…";
            try
            {
                await _state.RpcAsync("prompt.submit",
                    new JObject { ["session_id"] = SessionId, ["text"] = text });
            }
            catch (Exception ex)
            {
                AddSystem("Send failed: " + ex.Message);
                Streaming = false;
                BotStatus = "";
            }
        }

        private void StopTurn()
        {
            Streaming = false;
            BotStatus = "";
            _streamingItem = null;
        }

        // ---------- attachments ----------

        private async Task<string> AttachFileAsync(AttachedFile a)
        {
            try
            {
                var ext = Path.GetExtension(a.Name).ToLowerInvariant();
                string method;
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" ||
                    ext == ".webp" || ext == ".bmp") method = "image.attach_bytes";
                else if (ext == ".pdf") method = "pdf.attach";
                else method = "file.attach";

                byte[] bytes = File.ReadAllBytes(a.Path);
                string mime = MimeFor(ext);
                string dataUrl = "data:" + mime + ";base64," + Convert.ToBase64String(bytes);
                var res = await _state.RpcAsync(method,
                    new JObject { ["data"] = dataUrl, ["filename"] = a.Name });
                var r = res["ref"]?.ToString() ?? res["reference"]?.ToString()
                    ?? res["path"]?.ToString() ?? res["file"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(r) && res.Type == JTokenType.String) r = res.Value<string>();
                if (!string.IsNullOrEmpty(r) && !r.StartsWith("@file:")) r = "@file:" + r.TrimStart('@');
                Log.Info("Attached " + a.Name + " via " + method);
                return r;
            }
            catch (Exception ex)
            {
                AddSystem("Attachment failed (" + a.Name + "): " + ex.Message);
                return "";
            }
        }

        private static string MimeFor(string ext)
        {
            switch (ext)
            {
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                case ".bmp": return "image/bmp";
                case ".pdf": return "application/pdf";
                case ".txt": return "text/plain";
                case ".md": return "text/markdown";
                default: return "application/octet-stream";
            }
        }

        // ---------- streaming events ----------

        private ChatItem _streamingItem;
        private DateTime _streamStart;
        private int _streamChars;
        private long _turnInputTokens = -1;
        private long _turnOutputTokens = -1;
        private readonly Dictionary<string, ChatItem> _toolCards = new Dictionary<string, ChatItem>();

        private void OnGatewayEvent(object sender, GatewayEventArgs e)
        {
            if (e.SessionId != SessionId) return; // other sessions update their badges elsewhere
            var p = e.Payload;
            switch (e.Type)
            {
                case "gateway.ready":
                    RunOnUi(() => BotStatus = "");
                    break;
                case "message.delta":
                    {
                        var text = p["text"]?.ToString() ?? "";
                        // Bot-to-bot inbound rows render as notices instead of transcripts.
                        if (IsBotToBotInbound(text))
                        {
                            RunOnUi(() => Items.Add(new ChatItem
                            {
                                Kind = ChatItemKind.Notice,
                                Text = "Message from " + ExtractBotName(text),
                                ToolOutput = text
                            }));
                            break;
                        }
                        RunOnUi(() =>
                        {
                            if (_streamingItem == null)
                            {
                                _streamingItem = new ChatItem { Kind = ChatItemKind.Assistant };
                                Items.Add(_streamingItem);
                            }
                            _streamingItem.Text += text;
                            _streamChars += text.Length;
                        });
                        break;
                    }
                case "message.complete":
                    RunOnUi(() => FinalizeTurn(p));
                    break;
                case "tool.start":
                    RunOnUi(() => ToolStarted(p));
                    break;
                case "tool.complete":
                    RunOnUi(() => ToolCompleted(p));
                    break;
                case "session.usage":
                    _turnInputTokens = p["input_tokens"]?.Value<long>() ?? p["inputTokens"]?.Value<long>() ?? _turnInputTokens;
                    _turnOutputTokens = p["output_tokens"]?.Value<long>() ?? p["outputTokens"]?.Value<long>() ?? _turnOutputTokens;
                    break;
                default:
                    // Turn/phase events drive the live status line; unknown types are ignored.
                    var t = e.Type ?? "";
                    if (t.Contains("phase") || t.Contains("typing") || t.Contains("status"))
                    {
                        var label = p["label"]?.ToString() ?? p["text"]?.ToString() ?? t;
                        RunOnUi(() => BotStatus = label);
                    }
                    break;
            }
        }

        private void FinalizeTurn(JObject p)
        {
            _streamingItem = null;
            Streaming = false;
            BotStatus = "";
            var secs = Math.Max(0.1, (DateTime.Now - _streamStart).TotalSeconds);
            string stats;
            if (_turnOutputTokens >= 0)
            {
                long total = (_turnInputTokens >= 0 ? _turnInputTokens : 0) + _turnOutputTokens;
                stats = total + " tokens · " + Math.Round(_turnOutputTokens / secs) + " tok/s · " + secs.ToString("0.0") + "s";
            }
            else
            {
                long est = _streamChars / 4;
                stats = "~" + est + " tokens · " + Math.Round(est / secs) + " tok/s · " + secs.ToString("0.0") + "s";
            }
            Items.Add(new ChatItem { Kind = ChatItemKind.Stats, Text = stats });
            App.Tray?.NotifyTurnComplete(_state.ActiveGateway?.SelectedProfile ?? "Hermes", Title);
        }

        private void ToolStarted(JObject p)
        {
            var id = p["id"]?.ToString() ?? p["tool_call_id"]?.ToString() ?? Guid.NewGuid().ToString("N");
            var name = p["name"]?.ToString() ?? p["tool"]?.ToString() ?? "tool";
            var command = p["command"]?.ToString() ?? p["args"]?.ToString() ?? p["input"]?.ToString() ?? "";

            // Bot-to-bot: message_agent renders as "Messaging X…" instead of a shell transcript.
            if (name.IndexOf("message_agent", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var target = p["target"]?.ToString() ?? p["agent"]?.ToString() ?? p["to"]?.ToString() ?? "another bot";
                var notice = new ChatItem { Kind = ChatItemKind.Notice, Text = "Messaging " + target + "…" };
                _toolCards[id] = notice;
                Items.Add(notice);
                return;
            }

            ChatItem card;
            if (name.IndexOf("todo", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                card = new ChatItem { Kind = ChatItemKind.Todo, ToolName = name, Todos = ParseTodos(p) };
            }
            else
            {
                card = new ChatItem
                {
                    Kind = ChatItemKind.Tool,
                    ToolName = name,
                    ToolCommand = Cap(command, 500),
                    ToolRunning = true
                };
            }
            _toolCards[id] = card;
            Items.Add(card);
            BotStatus = name;
        }

        private void ToolCompleted(JObject p)
        {
            var id = p["id"]?.ToString() ?? p["tool_call_id"]?.ToString() ?? "";
            ChatItem card = null;
            if (!string.IsNullOrEmpty(id)) _toolCards.TryGetValue(id, out card);
            if (card == null && _toolCards.Count > 0)
                card = _toolCards.Values.LastOrDefault(c => c.ToolRunning || c.Kind == ChatItemKind.Notice);
            if (card == null) return;
            _toolCards.Remove(id);

            var output = p["output"]?.ToString() ?? p["result"]?.ToString() ?? "";
            if (card.Kind == ChatItemKind.Notice)
            {
                card.Text = card.Text.Replace("Messaging ", "Messaged ").TrimEnd('…');
                if (!string.IsNullOrEmpty(output)) card.ToolOutput = Cap(output, 2000);
            }
            else
            {
                card.ToolRunning = false;
                card.ToolOutput = Cap(output, 4000);
                if (card.Kind == ChatItemKind.Todo)
                    card.Todos = ParseTodos(p) ?? card.Todos;
            }
            BotStatus = "";
        }

        private static List<TodoEntry> ParseTodos(JObject p)
        {
            var arr = p["todos"] as JArray ?? p["items"] as JArray ?? p["tasks"] as JArray;
            if (arr == null) return new List<TodoEntry>();
            var list = new List<TodoEntry>();
            foreach (var t in arr)
            {
                if (t.Type == JTokenType.String) { list.Add(new TodoEntry { Text = t.Value<string>() }); continue; }
                var o = t as JObject;
                if (o == null) continue;
                var status = (o["status"]?.ToString() ?? o["done"]?.ToString() ?? "").ToLowerInvariant();
                list.Add(new TodoEntry
                {
                    Text = o["text"]?.ToString() ?? o["title"]?.ToString() ?? o["content"]?.ToString() ?? "",
                    Done = status == "done" || status == "completed" || status == "true"
                });
            }
            return list;
        }

        private static bool IsBotToBotInbound(string text)
        {
            return text.StartsWith("Message from", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractBotName(string text)
        {
            var t = text.Replace("Message from", "").Trim().TrimStart('🤖').Trim();
            var cut = t.IndexOfAny(new[] { ':', '\n' });
            return cut > 0 ? t.Substring(0, cut).Trim() : t;
        }

        private static string Cap(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n) + "\n… (truncated)";
        }

        // ---------- approvals & prompts ----------

        private void OnApproval(object sender, PendingApproval ap)
        {
            if (ap.SessionId == SessionId)
            {
                RunOnUi(() => Items.Add(new ChatItem
                {
                    Kind = ChatItemKind.Approval,
                    Text = ap.Title,
                    ToolCommand = ap.Command,
                    Approval = ap
                }));
            }
            // Other sessions: badge handled by MainViewModel -> Chats.MarkNeedsYou.
        }

        private void OnPrompt(object sender, PendingPrompt pr)
        {
            if (pr.SessionId != SessionId && !string.IsNullOrEmpty(pr.SessionId)) return;
            RunOnUi(() => Items.Add(new ChatItem
            {
                Kind = ChatItemKind.Prompt,
                Text = pr.Title,
                ToolOutput = pr.Prompt,
                Prompt = pr
            }));
        }

        private async Task AnswerApprovalAsync(ChatItem item, string choice)
        {
            if (item?.Approval == null) return;
            try
            {
                await _state.AnswerApprovalAsync(item.Approval, choice);
                RunOnUi(() =>
                {
                    item.Answered = true;
                    item.Text += " — " + choice;
                    _main.Chats.ClearNeedsYou(item.Approval.SessionId);
                });
            }
            catch (Exception ex) { AddSystem("Approval answer failed: " + ex.Message); }
        }

        private async Task AnswerPromptAsync(ChatItem item)
        {
            // The view passes the typed value via the item's ToolCommand property (bound to the input box).
            if (item?.Prompt == null) return;
            try
            {
                await _state.AnswerPromptAsync(item.Prompt, item.ToolCommand ?? "");
                RunOnUi(() => { item.Answered = true; item.ToolCommand = ""; });
            }
            catch (Exception ex) { AddSystem("Prompt answer failed: " + ex.Message); }
        }

        private async Task AnswerLatestApprovalAsync(string choice)
        {
            var latest = Items.LastOrDefault(i => i.Kind == ChatItemKind.Approval && !i.Answered);
            if (latest == null) { AddSystem("No pending approval in this chat."); return; }
            await AnswerApprovalAsync(latest, choice);
        }

        // ---------- slash autocomplete ----------

        private async void UpdateSuggestions()
        {
            var word = CurrentWord();
            if (word == null || !word.StartsWith("/"))
            {
                ShowSuggestions = false;
                return;
            }
            if (!_catalogLoaded)
            {
                _catalogLoaded = true;
                try
                {
                    var cat = await _state.RpcAsync("commands.catalog", new JObject());
                    foreach (var t in HomeViewModel.AsArray(cat["commands"] ?? cat))
                    {
                        var n = (t as JObject)?["name"]?.ToString();
                        if (!string.IsNullOrEmpty(n)) _catalogCommands.Add("/" + n.TrimStart('/'));
                    }
                }
                catch { }
            }
            var all = LocalSlash.Concat(_catalogCommands).Distinct()
                .Where(c => c.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                .Take(8).ToList();
            RunOnUi(() =>
            {
                Suggestions.Clear();
                foreach (var s in all) Suggestions.Add(s);
                ShowSuggestions = Suggestions.Count > 0;
            });
        }

        private string CurrentWord()
        {
            var text = ComposerText ?? "";
            if (string.IsNullOrEmpty(text)) return null;
            int i = text.Length - 1;
            while (i >= 0 && !char.IsWhiteSpace(text[i])) i--;
            var word = text.Substring(i + 1);
            return string.IsNullOrEmpty(word) ? null : word;
        }

        private void ApplySuggestion(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            var text = ComposerText ?? "";
            int i = text.Length - 1;
            while (i >= 0 && !char.IsWhiteSpace(text[i])) i--;
            ComposerText = text.Substring(0, i + 1) + s + " ";
            ShowSuggestions = false;
        }

        // ---------- model picker ----------

        private async void LoadModelOptions()
        {
            if (_state.Rest == null) return;
            try
            {
                var tok = await _state.Rest.GetAsync("/api/model/options");
                var groups = new List<ModelGroup>();
                Action<string, JArray> addGroup = (provider, arr) =>
                {
                    var g = new ModelGroup { Provider = provider, Slug = provider };
                    foreach (var m in arr)
                    {
                        if (m.Type == JTokenType.String)
                            g.Models.Add(new ModelOption { Provider = provider, Slug = provider, Label = m.Value<string>() });
                        else
                        {
                            var o = m as JObject;
                            g.Models.Add(new ModelOption
                            {
                                Provider = provider,
                                Slug = o?["provider"]?.ToString() ?? o?["slug"]?.ToString() ?? provider,
                                Label = o?["id"]?.ToString() ?? o?["name"]?.ToString() ?? o?["model"]?.ToString() ?? "?"
                            });
                        }
                    }
                    if (g.Models.Count > 0) groups.Add(g);
                };
                if (tok is JObject)
                {
                    var o = (JObject)tok;
                    var providers = o["providers"] as JArray;
                    if (providers != null)
                    {
                        foreach (var pv in providers)
                        {
                            var po = pv as JObject;
                            var name = po?["name"]?.ToString() ?? po?["slug"]?.ToString() ?? "provider";
                            var slug = po?["slug"]?.ToString() ?? name;
                            var arr = po?["models"] as JArray ?? po?["options"] as JArray;
                            if (arr != null)
                            {
                                var g = new ModelGroup { Provider = name, Slug = slug };
                                foreach (var m in arr)
                                    g.Models.Add(new ModelOption
                                    {
                                        Provider = name,
                                        Slug = m["provider"]?.ToString() ?? slug,
                                        Label = m["id"]?.ToString() ?? m["name"]?.ToString() ?? "?"
                                    });
                                if (g.Models.Count > 0) groups.Add(g);
                            }
                        }
                    }
                    else
                    {
                        foreach (var prop in o.Properties())
                            if (prop.Value is JArray) addGroup(prop.Name, (JArray)prop.Value);
                    }
                }
                RunOnUi(() =>
                {
                    ModelGroups.Clear();
                    foreach (var g in groups) ModelGroups.Add(g);
                });
            }
            catch (Exception ex) { Log.Warn("Model options failed: " + ex.Message); }
        }

        private async Task PickModelAsync(ModelOption opt)
        {
            if (opt == null) return;
            ShowModelPicker = false;
            try
            {
                // Session-scoped change; never model.default.
                var value = opt.Label + " --provider " + opt.Slug + " --session";
                await _state.RpcAsync("config.set", new JObject { ["key"] = "model", ["value"] = value });
                CurrentModel = opt.Label;
                AddSystem("Model set to " + opt.Label + " (this session).");
            }
            catch (Exception ex) { AddSystem("Model change failed: " + ex.Message); }
        }

        private void AddSystem(string text)
        {
            RunOnUi(() => Items.Add(new ChatItem { Kind = ChatItemKind.System, Text = text }));
        }
    }
}
