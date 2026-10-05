using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using VoryWindows.Models;
using VoryWindows.Services;

namespace VoryWindows.Networking
{
    public class GatewayEventArgs : EventArgs
    {
        public string Type { get; set; } = "";
        public string SessionId { get; set; } = "";
        public JObject Payload { get; set; } = new JObject();
        public int Seq { get; set; }
    }

    public class ServerRequestArgs : EventArgs
    {
        public string RequestId { get; set; } = "";
        public string Method { get; set; } = "";
        public JObject Params { get; set; } = new JObject();
    }

    /// <summary>
    /// JSON-RPC 2.0 client over /api/ws. Integer ids for client requests, string ids for
    /// server requests, event notifications. Exponential-backoff reconnect; re-mints the
    /// /api/auth/ws-ticket on every attempt for bearer auth.
    /// </summary>
    public class HermesSocketClient : IDisposable
    {
        private readonly GatewayCredential _gateway;
        private readonly Func<Task<string>> _mintWsTicket;
        private ClientWebSocket _ws;
        private CancellationTokenSource _loopCts;
        private Task _loopTask;
        private int _nextId;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JObject>> _pending =
            new ConcurrentDictionary<int, TaskCompletionSource<JObject>>();
        private readonly object _reconnectLock = new object();
        private bool _disposed;
        private bool _wantConnection;
        private int _reconnectAttempt;

        public event EventHandler<GatewayEventArgs> GatewayEvent;
        public event EventHandler<ServerRequestArgs> ServerRequest;
        public event EventHandler<ConnectionState> StateChanged;

        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        public HermesSocketClient(GatewayCredential gateway, Func<Task<string>> mintWsTicket)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _mintWsTicket = mintWsTicket;
        }

        private void SetState(ConnectionState s)
        {
            State = s;
            try { StateChanged?.Invoke(this, s); } catch { }
        }

        public async Task ConnectAsync(CancellationToken ct)
        {
            _wantConnection = true;
            _reconnectAttempt = 0;
            await ConnectOnceAsync(ct).ConfigureAwait(false);
        }

        private async Task<string> BuildWsUrlAsync()
        {
            var url = UrlUtil.ToWsUrl(_gateway.BaseUrl, "/api/ws");
            var sep = url.Contains("?") ? "&" : "?";
            if (_gateway.AuthMode == AuthMode.SessionToken && !string.IsNullOrEmpty(_gateway.SessionToken))
                return url + sep + "token=" + Uri.EscapeDataString(_gateway.SessionToken);
            if (_gateway.UsesBearer && _mintWsTicket != null)
            {
                try
                {
                    var ticket = await _mintWsTicket().ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(ticket))
                        return url + sep + "ticket=" + Uri.EscapeDataString(ticket);
                }
                catch (Exception ex)
                {
                    Log.Info("ws-ticket mint failed, falling back to Authorization header: " + ex.Message);
                }
            }
            return url;
        }

        private async Task ConnectOnceAsync(CancellationToken ct)
        {
            if (_disposed || !_wantConnection) return;
            lock (_reconnectLock)
            {
                if (_loopTask != null && !_loopTask.IsCompleted) return;
            }
            SetState(_reconnectAttempt == 0 ? ConnectionState.Connecting : ConnectionState.Reconnecting);

            var ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            if (_gateway.HasCfAccess)
            {
                ws.Options.SetRequestHeader("CF-Access-Client-Id", _gateway.CfAccessId);
                ws.Options.SetRequestHeader("CF-Access-Client-Secret", _gateway.CfAccessSecret);
            }
            if (_gateway.UsesBearer && !string.IsNullOrEmpty(_gateway.AccessToken))
                ws.Options.SetRequestHeader("Authorization", "Bearer " + _gateway.AccessToken);
            else if (_gateway.AuthMode == AuthMode.SessionToken && !string.IsNullOrEmpty(_gateway.SessionToken))
                ws.Options.SetRequestHeader("X-Hermes-Session-Token", _gateway.SessionToken);

            string url;
            try { url = await BuildWsUrlAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                Log.Info("WS connect aborted: " + ex.Message);
                ScheduleReconnect();
                return;
            }

            try
            {
                Log.Info("WS connecting to " + UrlUtil.Redact(url));
                await ws.ConnectAsync(new Uri(url), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Info("WS connect failed: " + ex.Message);
                ws.Dispose();
                ScheduleReconnect();
                return;
            }

            _ws = ws;
            _reconnectAttempt = 0;
            SetState(ConnectionState.Connected);
            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _loopTask = Task.Run(() => ReceiveLoopAsync(_loopCts.Token));
        }

        private void ScheduleReconnect()
        {
            if (_disposed || !_wantConnection) { SetState(ConnectionState.Failed); return; }
            _reconnectAttempt++;
            // Exponential backoff: 1s, 2s, 4s ... capped at 30s.
            int delay = Math.Min(30000, (int)(1000 * Math.Pow(2, _reconnectAttempt - 1)));
            SetState(ConnectionState.Reconnecting);
            Log.Info("WS reconnect attempt " + _reconnectAttempt + " in " + delay + "ms");
            Task.Delay(delay).ContinueWith(t =>
            {
                if (!_disposed && _wantConnection)
                    ConnectOnceAsync(CancellationToken.None).ConfigureAwait(false);
            });
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[65536];
            var sb = new StringBuilder();
            try
            {
                while (!ct.IsCancellationRequested && _ws != null &&
                       _ws.State == WebSocketState.Open)
                {
                    sb.Clear();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            Log.Info("WS closed by server");
                            await TryCloseAsync().ConfigureAwait(false);
                            ScheduleReconnect();
                            return;
                        }
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Text)
                        HandleFrame(sb.ToString());
                }
            }
            catch (OperationCanceledException) { /* shutting down */ }
            catch (Exception ex)
            {
                Log.Info("WS receive loop ended: " + ex.Message);
            }
            if (_wantConnection && !_disposed)
                ScheduleReconnect();
        }

        private void HandleFrame(string text)
        {
            JObject frame;
            try { frame = JObject.Parse(text); }
            catch { return; }

            switch (JsonRpc.Classify(frame))
            {
                case JsonRpc.FrameKind.Response:
                    {
                        var idTok = frame["id"];
                        int id = idTok != null && idTok.Type == JTokenType.Integer ? idTok.Value<int>() : -1;
                        TaskCompletionSource<JObject> tcs;
                        if (id >= 0 && _pending.TryRemove(id, out tcs))
                        {
                            if (frame["error"] != null)
                                tcs.TrySetException(new JsonRpcException(
                                    frame["error"]["code"]?.Value<int>() ?? -32000,
                                    frame["error"]["message"]?.ToString() ?? "RPC error"));
                            else
                                tcs.TrySetResult(frame);
                        }
                        break;
                    }
                case JsonRpc.FrameKind.EventNotification:
                    {
                        var p = frame["params"] as JObject ?? new JObject();
                        var args = new GatewayEventArgs
                        {
                            Type = p["type"]?.ToString() ?? "",
                            SessionId = p["session_id"]?.ToString() ?? "",
                            Payload = p["payload"] as JObject ?? new JObject(),
                            Seq = p["seq"]?.Value<int>() ?? 0
                        };
                        try { GatewayEvent?.Invoke(this, args); } catch { }
                        break;
                    }
                case JsonRpc.FrameKind.ServerRequest:
                    {
                        var args = new ServerRequestArgs
                        {
                            RequestId = frame["id"]?.ToString() ?? "",
                            Method = frame["method"]?.ToString() ?? "",
                            Params = frame["params"] as JObject ?? new JObject()
                        };
                        try { ServerRequest?.Invoke(this, args); } catch { }
                        break;
                    }
            }
            // Unknown frames are ignored per protocol.
        }

        /// <summary>Sends a request with an incrementing integer id; awaits the matching response (120s).</summary>
        public async Task<JObject> RequestAsync(string method, JObject parameters, int timeoutMs = 120000)
        {
            if (_ws == null || _ws.State != WebSocketState.Open)
                throw new InvalidOperationException("WebSocket is not connected.");
            int id = Interlocked.Increment(ref _nextId);
            var tcs = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;
            var text = JsonRpc.BuildRequest(id, method, parameters);
            try
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TaskCompletionSource<JObject> removed;
                _pending.TryRemove(id, out removed);
                throw new InvalidOperationException("Send failed: " + ex.Message, ex);
            }

            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                using (cts.Token.Register(() =>
                {
                    TaskCompletionSource<JObject> removed;
                    if (_pending.TryRemove(id, out removed))
                        removed.TrySetException(new TimeoutException("Request '" + method + "' timed out."));
                }))
                {
                    var frame = await tcs.Task.ConfigureAwait(false);
                    return frame["result"] as JObject ?? new JObject();
                }
            }
        }

        /// <summary>Answers a server-initiated request (string id) with {"choice": ...} or {value}.</summary>
        public async Task RespondAsync(string requestId, JObject result)
        {
            if (_ws == null || _ws.State != WebSocketState.Open)
                throw new InvalidOperationException("WebSocket is not connected.");
            var text = JsonRpc.BuildResponse(requestId, result);
            var bytes = Encoding.UTF8.GetBytes(text);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task TryCloseAsync()
        {
            try
            {
                if (_ws != null && (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived))
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closing",
                        CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
        }

        public async Task DisconnectAsync()
        {
            _wantConnection = false;
            try { if (_loopCts != null) _loopCts.Cancel(); } catch { }
            await TryCloseAsync().ConfigureAwait(false);
            SetState(ConnectionState.Disconnected);
        }

        public void Dispose()
        {
            _disposed = true;
            _wantConnection = false;
            try { if (_loopCts != null) _loopCts.Cancel(); } catch { }
            try { if (_ws != null) _ws.Dispose(); } catch { }
            foreach (var kv in _pending)
                kv.Value.TrySetCanceled();
            _pending.Clear();
        }
    }
}
