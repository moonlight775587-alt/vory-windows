using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using VoryWindows.Auth;
using VoryWindows.Models;
using VoryWindows.Mvvm;
using VoryWindows.Networking;

namespace VoryWindows.Services
{
    public class TestLegResult
    {
        public string Name { get; set; } = "";
        public bool Passed { get; set; }
        public string Detail { get; set; } = "";
    }

    /// <summary>
    /// Central app state: gateways, active gateway, REST + socket clients, connection
    /// lifecycle, token refresh, pending approvals/prompts, 503 probe.
    /// </summary>
    public class AppState : ViewModelBase
    {
        private readonly GatewayStore _store = new GatewayStore();

        public ObservableCollection<GatewayCredential> Gateways { get; } =
            new ObservableCollection<GatewayCredential>();

        private GatewayCredential _activeGateway;
        public GatewayCredential ActiveGateway
        {
            get { return _activeGateway; }
            set
            {
                if (Set(ref _activeGateway, value))
                {
                    RebuildClients();
                    OnPropertyChanged(nameof(IsConnected));
                }
            }
        }

        public HermesRestClient Rest { get; private set; }
        public HermesSocketClient Socket { get; private set; }

        private ConnectionState _connectionState = ConnectionState.Disconnected;
        public ConnectionState ConnectionState
        {
            get { return _connectionState; }
            private set
            {
                // May be raised from socket threads: marshal to the UI thread.
                RunOnUi(() => { Set(ref _connectionState, value); OnPropertyChanged(nameof(IsConnected)); });
            }
        }

        public bool IsConnected { get { return ConnectionState == ConnectionState.Connected; } }

        private bool _restartRequired;
        public bool RestartRequired
        {
            get { return _restartRequired; }
            set { Set(ref _restartRequired, value); }
        }

        private string _restartDetail = "";
        public string RestartDetail
        {
            get { return _restartDetail; }
            set { Set(ref _restartDetail, value); }
        }

        private string _userName = "";
        public string UserName
        {
            get { return _userName; }
            set { Set(ref _userName, value); SavePrefs(); }
        }

        private string _accent = "#5B8DEF";
        public string Accent
        {
            get { return _accent; }
            set { Set(ref _accent, value); SavePrefs(); }
        }

        private bool _darkTheme = true;
        public bool DarkTheme
        {
            get { return _darkTheme; }
            set { Set(ref _darkTheme, value); SavePrefs(); }
        }

        public event EventHandler<PendingApproval> ApprovalRequested;
        public event EventHandler<PendingPrompt> PromptRequested;
        public event EventHandler<GatewayEventArgs> GatewayEvent;

        public AppState()
        {
            foreach (var g in _store.Load()) Gateways.Add(g);
            LoadPrefs();
            var first = Gateways.FirstOrDefault();
            if (first != null) ActiveGateway = first;
        }

        public void SaveGateways()
        {
            _store.Save(Gateways.ToList());
        }

        private string PrefsPath()
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VoryWindows", "prefs.json");
        }

        private void LoadPrefs()
        {
            try
            {
                var p = PrefsPath();
                if (!System.IO.File.Exists(p)) return;
                var o = JObject.Parse(System.IO.File.ReadAllText(p));
                _userName = o["userName"]?.ToString() ?? "";
                _accent = o["accent"]?.ToString() ?? "#5B8DEF";
                _darkTheme = o["darkTheme"]?.Value<bool>() ?? true;
            }
            catch { }
        }

        private void SavePrefs()
        {
            try
            {
                var o = new JObject
                {
                    ["userName"] = _userName,
                    ["accent"] = _accent,
                    ["darkTheme"] = _darkTheme
                };
                System.IO.File.WriteAllText(PrefsPath(), o.ToString());
            }
            catch { }
        }

        private void RebuildClients()
        {
            try { if (Socket != null) Socket.Dispose(); } catch { }
            try { if (Rest != null) Rest.Dispose(); } catch { }
            Socket = null;
            Rest = null;
            if (_activeGateway == null) return;

            Rest = new HermesRestClient(_activeGateway, RefreshTokensAsync);
            Socket = new HermesSocketClient(_activeGateway, MintWsTicketAsync);
            Socket.StateChanged += (s, st) => RunOnUi(() => ConnectionState = st);
            Socket.GatewayEvent += (s, e) =>
            {
                try { GatewayEvent?.Invoke(this, e); } catch { }
            };
            Socket.ServerRequest += (s, e) => HandleServerRequest(e);
        }

        private void HandleServerRequest(ServerRequestArgs e)
        {
            var method = e.Method ?? "";
            var p = e.Params;
            if (method == "approval.request" || method.EndsWith(".approval.request"))
            {
                var ap = new PendingApproval
                {
                    RequestId = e.RequestId,
                    SessionId = p["session_id"]?.ToString() ?? "",
                    Title = p["title"]?.ToString() ?? "Approval requested",
                    Command = p["command"]?.ToString() ?? p["tool"]?.ToString() ?? "",
                    Detail = p["detail"]?.ToString() ?? p["description"]?.ToString() ?? "",
                    Queued = p["queued"]?.Value<bool>() ?? false
                };
                RunOnUi(() => { try { ApprovalRequested?.Invoke(this, ap); } catch { } });
            }
            else if (method.Contains("secret") || method.Contains("clarify") ||
                     method.Contains("sudo") || method.Contains("vault"))
            {
                bool secret = method.Contains("secret") || method.Contains("sudo") || method.Contains("vault");
                var pr = new PendingPrompt
                {
                    RequestId = e.RequestId,
                    SessionId = p["session_id"]?.ToString() ?? "",
                    Kind = secret ? (method.Contains("clarify") ? "clarify" : "secret") : "clarify",
                    Title = p["title"]?.ToString() ?? "Input requested",
                    Prompt = p["prompt"]?.ToString() ?? p["message"]?.ToString() ?? "",
                    IsSecret = secret
                };
                RunOnUi(() => { try { PromptRequested?.Invoke(this, pr); } catch { } });
            }
            else
            {
                Log.Info("Unhandled server request: " + method);
            }
        }

        public async Task<bool> RefreshTokensAsync()
        {
            var g = _activeGateway;
            if (g == null || !g.UsesBearer || string.IsNullOrEmpty(g.RefreshToken)) return false;
            var flow = new NativeAuthFlow(g.BaseUrl);
            var pair = await flow.RefreshAsync(g.RefreshToken).ConfigureAwait(false);
            if (pair == null || string.IsNullOrEmpty(pair.AccessToken)) return false;
            g.AccessToken = pair.AccessToken;
            if (!string.IsNullOrEmpty(pair.RefreshToken)) g.RefreshToken = pair.RefreshToken;
            RunOnUi(SaveGateways);
            return true;
        }

        private async Task<string> MintWsTicketAsync()
        {
            if (Rest == null) return "";
            var tok = await Rest.GetAsync("/api/auth/ws-ticket").ConfigureAwait(false);
            return tok["ticket"]?.ToString() ?? tok["ws_ticket"]?.ToString() ?? tok.ToString();
        }

        /// <summary>Connect REST + socket. Probes /api/model/options for the 503 restart banner.</summary>
        public async Task ConnectAsync()
        {
            if (Rest == null || Socket == null || _activeGateway == null) return;
            ConnectionState = ConnectionState.Connecting;
            try
            {
                await ProbeRestartRequiredAsync().ConfigureAwait(false);
                await Socket.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warn("Connect failed: " + ex.Message);
                ConnectionState = ConnectionState.Failed;
            }
        }

        public async Task DisconnectAsync()
        {
            if (Socket != null) await Socket.DisconnectAsync().ConfigureAwait(false);
        }

        public async Task ProbeRestartRequiredAsync()
        {
            if (Rest == null) return;
            try
            {
                await Rest.GetAsync("/api/model/options").ConfigureAwait(false);
                RunOnUi(() => { RestartRequired = false; RestartDetail = ""; });
            }
            catch (ApiException ex) when (ex.IsRestartRequired)
            {
                RunOnUi(() =>
                {
                    RestartRequired = true;
                    RestartDetail = "The dashboard is serving code older than its checkout.";
                });
            }
            catch { /* other errors are surfaced by the 3-leg test instead */ }
        }

        /// <summary>3-leg connection test. All legs must pass before Save is enabled.</summary>
        public async Task<List<TestLegResult>> TestGatewayAsync(GatewayCredential g)
        {
            var legs = new List<TestLegResult>();
            var probe = new HermesRestClient(g, null);
            try
            {
                // Leg 1: /api/status returns JSON starting with {"version":
                try
                {
                    var st = await probe.GetAsync("/api/status").ConfigureAwait(false);
                    bool ok = st is JObject && st["version"] != null;
                    legs.Add(new TestLegResult
                    {
                        Name = "Dashboard API",
                        Passed = ok,
                        Detail = ok ? "version " + st["version"] : "Unexpected body from /api/status"
                    });
                }
                catch (ApiException ex)
                {
                    legs.Add(new TestLegResult
                    {
                        Name = "Dashboard API",
                        Passed = false,
                        Detail = ex.IsHtmlBody
                            ? "HTML returned - wrong URL, login page, or Cloudflare Access in the way."
                            : ex.Message
                    });
                }

                // Leg 2: credential accepted
                if (legs[0].Passed)
                {
                    try
                    {
                        JToken me;
                        try { me = await probe.GetAsync("/api/auth/me").ConfigureAwait(false); }
                        catch (ApiException ex2) when ((int)ex2.StatusCode == 404)
                        { me = await probe.GetAsync("/api/profiles").ConfigureAwait(false); }
                        legs.Add(new TestLegResult
                        {
                            Name = "Credentials",
                            Passed = true,
                            Detail = "Accepted" + (me is JObject && me["username"] != null ? " as " + me["username"] : "")
                        });
                    }
                    catch (ApiException ex)
                    {
                        legs.Add(new TestLegResult
                        {
                            Name = "Credentials",
                            Passed = false,
                            Detail = (int)ex.StatusCode == 401 ? "Rejected (401). Check the token / sign in again." : ex.Message
                        });
                    }
                }
                else
                {
                    legs.Add(new TestLegResult { Name = "Credentials", Passed = false, Detail = "Skipped - leg 1 failed." });
                }

                // Leg 3: wss opens and gateway.ready arrives
                if (legs[0].Passed && legs[1].Passed)
                {
                    legs.Add(await TestWebSocketAsync(g).ConfigureAwait(false));
                }
                else
                {
                    legs.Add(new TestLegResult { Name = "WebSocket", Passed = false, Detail = "Skipped - earlier leg failed." });
                }
            }
            finally
            {
                probe.Dispose();
            }
            return legs;
        }

        private async Task<TestLegResult> TestWebSocketAsync(GatewayCredential g)
        {
            var leg = new TestLegResult { Name = "WebSocket" };
            var readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<Task<string>> ticket = async () =>
            {
                var probe = new HermesRestClient(g, null);
                try
                {
                    var tok = await probe.GetAsync("/api/auth/ws-ticket").ConfigureAwait(false);
                    return tok["ticket"]?.ToString() ?? "";
                }
                finally { probe.Dispose(); }
            };
            var sock = new HermesSocketClient(g, g.UsesBearer ? ticket : (Func<Task<string>>)null);
            EventHandler<GatewayEventArgs> onEvent = (s, e) =>
            {
                if (e.Type == "gateway.ready") readyTcs.TrySetResult(true);
            };
            sock.GatewayEvent += onEvent;
            try
            {
                await sock.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
                var winner = await Task.WhenAny(readyTcs.Task, Task.Delay(12000)).ConfigureAwait(false);
                if (winner == readyTcs.Task && readyTcs.Task.Result)
                {
                    leg.Passed = true;
                    leg.Detail = "wss://…/api/ws open, gateway.ready received.";
                }
                else
                {
                    leg.Passed = false;
                    leg.Detail = "Socket opened but no gateway.ready in 12s. Check proxy Upgrade forwarding, " +
                                 "Cloudflare Access on the socket, or dashboard.public_url vs the typed host.";
                }
            }
            catch (Exception ex)
            {
                leg.Passed = false;
                leg.Detail = "WebSocket failed: " + ex.Message;
            }
            finally
            {
                sock.GatewayEvent -= onEvent;
                await sock.DisconnectAsync().ConfigureAwait(false);
                sock.Dispose();
            }
            return leg;
        }

        public async Task AnswerApprovalAsync(PendingApproval ap, string choice)
        {
            if (Socket == null) return;
            var result = new JObject { ["choice"] = choice };
            if (ap.Queued)
            {
                await Socket.RequestAsync("approval.respond",
                    new JObject { ["approval_id"] = ap.RequestId, ["choice"] = choice }).ConfigureAwait(false);
            }
            else
            {
                await Socket.RespondAsync(ap.RequestId, result).ConfigureAwait(false);
            }
            Log.Info("Approval " + ap.RequestId + " answered: " + choice);
        }

        public async Task AnswerPromptAsync(PendingPrompt pr, string value)
        {
            if (Socket == null) return;
            // Secret values are never logged.
            await Socket.RespondAsync(pr.RequestId, new JObject { ["value"] = value }).ConfigureAwait(false);
            Log.Info("Prompt " + pr.RequestId + " (" + pr.Kind + ") answered.");
        }

        public async Task<JObject> RpcAsync(string method, JObject parameters)
        {
            if (Socket == null) throw new InvalidOperationException("Not connected.");
            try
            {
                return await Socket.RequestAsync(method, parameters ?? new JObject()).ConfigureAwait(false);
            }
            catch (JsonRpcException ex) when (ex.IsMethodNotFound)
            {
                throw new InvalidOperationException("This gateway does not offer '" + method + "' (-32601).");
            }
        }
    }
}
