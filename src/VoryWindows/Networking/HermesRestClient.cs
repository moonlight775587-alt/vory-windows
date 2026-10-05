using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using VoryWindows.Models;
using VoryWindows.Services;

namespace VoryWindows.Networking
{
    public class ApiException : Exception
    {
        public HttpStatusCode StatusCode { get; }
        public string Body { get; }
        public bool IsRestartRequired { get; }
        public bool IsHtmlBody { get; }

        public ApiException(HttpStatusCode status, string body, bool isHtml, bool restartRequired)
            : base(BuildMessage(status, body, isHtml, restartRequired))
        {
            StatusCode = status;
            Body = body;
            IsHtmlBody = isHtml;
            IsRestartRequired = restartRequired;
        }

        private static string BuildMessage(HttpStatusCode status, string body, bool isHtml, bool restart)
        {
            if (isHtml)
                return "The server returned an HTML page, not the dashboard API. " +
                       "Check the gateway URL (not a login page / proxy / Access page).";
            if (restart)
                return "503 Restart required: the dashboard is serving code older than its checkout.";
            var snippet = (body ?? "").Trim();
            if (snippet.Length > 400) snippet = snippet.Substring(0, 400) + "…";
            return string.IsNullOrWhiteSpace(snippet)
                ? "HTTP " + (int)status + " " + status
                : "HTTP " + (int)status + ": " + snippet;
        }
    }

    /// <summary>
    /// REST client for the Hermes dashboard API. Applies ?profile= scoping, auth headers,
    /// 401 -&gt; refresh -&gt; retry, HTML-body detection and 503 restart detection.
    /// </summary>
    public class HermesRestClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly GatewayCredential _gateway;
        private readonly Func<Task<bool>> _refreshTokens;
        private bool _refreshAttempted;

        public HermesRestClient(GatewayCredential gateway, Func<Task<bool>> refreshTokens)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _refreshTokens = refreshTokens;
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        }

        public string BaseUrl { get { return _gateway.BaseUrl; } }
        public string Profile { get { return _gateway.SelectedProfile; } }

        private string BuildUrl(string path, IDictionary<string, string> query)
        {
            var sb = new StringBuilder();
            sb.Append(_gateway.BaseUrl.TrimEnd('/'));
            if (!path.StartsWith("/")) sb.Append('/');
            sb.Append(path);

            var q = new List<string>();
            if (NeedsProfile(path))
                q.Add("profile=" + Uri.EscapeDataString(_gateway.SelectedProfile ?? "default"));
            if (query != null)
            {
                foreach (var kv in query)
                    q.Add(Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value ?? ""));
            }
            if (q.Count > 0) { sb.Append('?'); sb.Append(string.Join("&", q)); }
            return sb.ToString();
        }

        private static bool NeedsProfile(string path)
        {
            // Auth/status bootstrap calls are unscoped per the protocol.
            if (path.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase)) return false;
            if (path.Equals("/api/status", StringComparison.OrdinalIgnoreCase)) return false;
            if (path.Equals("/api/health", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private void ApplyAuth(HttpRequestMessage req)
        {
            if (_gateway.AuthMode == AuthMode.SessionToken)
            {
                if (!string.IsNullOrEmpty(_gateway.SessionToken))
                    req.Headers.Add("X-Hermes-Session-Token", _gateway.SessionToken);
            }
            else if (!string.IsNullOrEmpty(_gateway.AccessToken))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _gateway.AccessToken);
            }
            if (_gateway.HasCfAccess)
            {
                req.Headers.Add("CF-Access-Client-Id", _gateway.CfAccessId);
                req.Headers.Add("CF-Access-Client-Secret", _gateway.CfAccessSecret);
            }
        }

        private async Task<JToken> SendAsync(HttpMethod method, string path, IDictionary<string, string> query,
            HttpContent content, bool retried)
        {
            var url = BuildUrl(path, query);
            using (var req = new HttpRequestMessage(method, url))
            {
                ApplyAuth(req);
                if (content != null) req.Content = content;
                HttpResponseMessage resp;
                try
                {
                    resp = await _http.SendAsync(req).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw new ApiException(0, ex.Message, false, false);
                }
                using (resp)
                {
                    if (resp.StatusCode == HttpStatusCode.Unauthorized
                        && _gateway.UsesBearer && !retried && _refreshTokens != null)
                    {
                        Log.Info("REST 401 - attempting token refresh then retry");
                        bool ok = false;
                        try { ok = await _refreshTokens().ConfigureAwait(false); } catch { ok = false; }
                        if (ok) return await SendAsync(method, path, query, content, true).ConfigureAwait(false);
                    }

                    string body = "";
                    try { body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false); }
                    catch { /* ignore */ }

                    bool isHtml = LooksLikeHtml(resp, body);
                    bool restart = resp.StatusCode == HttpStatusCode.ServiceUnavailable
                        && (body ?? "").IndexOf("Restart required", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!resp.IsSuccessStatusCode)
                        throw new ApiException(resp.StatusCode, body, isHtml, restart);
                    if (isHtml)
                        throw new ApiException(resp.StatusCode, body, true, false);

                    if (string.IsNullOrWhiteSpace(body)) return new JObject();
                    try { return JToken.Parse(body); }
                    catch
                    {
                        throw new ApiException(resp.StatusCode, body, LooksLikeHtml(resp, body), false);
                    }
                }
            }
        }

        private static bool LooksLikeHtml(HttpResponseMessage resp, string body)
        {
            var ct = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (ct.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            var t = (body ?? "").TrimStart();
            return t.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
        }

        public Task<JToken> GetAsync(string path, IDictionary<string, string> query = null)
        {
            return SendAsync(HttpMethod.Get, path, query, null, false);
        }

        public Task<JToken> PostJsonAsync(string path, JObject body, IDictionary<string, string> query = null)
        {
            var content = new StringContent((body ?? new JObject()).ToString(), Encoding.UTF8, "application/json");
            return SendAsync(HttpMethod.Post, path, query, content, false);
        }

        public Task<JToken> PutJsonAsync(string path, JObject body, IDictionary<string, string> query = null)
        {
            var content = new StringContent((body ?? new JObject()).ToString(), Encoding.UTF8, "application/json");
            return SendAsync(HttpMethod.Put, path, query, content, false);
        }

        public Task<JToken> DeleteJsonAsync(string path, JObject body = null, IDictionary<string, string> query = null)
        {
            HttpContent content = null;
            if (body != null)
                content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
            return SendAsync(HttpMethod.Delete, path, query, content, false);
        }

        public async Task UploadFileAsync(string remoteDir, string fileName, Stream data, string contentType)
        {
            var url = BuildUrl("/api/files/upload-stream", null);
            // Rebuild without helper to keep profile scoping identical to other calls.
            using (var form = new MultipartFormDataContent())
            using (var streamContent = new StreamContent(data))
            {
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType ?? "application/octet-stream");
                form.Add(streamContent, "file", fileName);
                if (!string.IsNullOrEmpty(remoteDir))
                    form.Add(new StringContent(remoteDir), "path");
                using (var req = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    ApplyAuth(req);
                    req.Content = form;
                    using (var resp = await _http.SendAsync(req).ConfigureAwait(false))
                    {
                        string body = "";
                        try { body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false); } catch { }
                        if (!resp.IsSuccessStatusCode)
                            throw new ApiException(resp.StatusCode, body, LooksLikeHtml(resp, body), false);
                    }
                }
            }
        }

        public async Task DownloadFileAsync(string remotePath, Stream destination)
        {
            var url = BuildUrl("/api/files/download",
                new Dictionary<string, string> { { "path", remotePath } });
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                ApplyAuth(req);
                using (var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        string body = "";
                        try { body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false); } catch { }
                        throw new ApiException(resp.StatusCode, body, LooksLikeHtml(resp, body), false);
                    }
                    using (var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        await src.CopyToAsync(destination).ConfigureAwait(false);
                    }
                }
            }
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
