using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using VoryWindows.Models;
using VoryWindows.Services;

namespace VoryWindows.Auth
{
    public class TokenPair
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public JObject Raw { get; set; } = new JObject();
    }

    /// <summary>
    /// RFC 8252 native flow for the basic-auth provider:
    /// POST /auth/native/authorize -&gt; POST /auth/password-login -&gt; POST /auth/native/token.
    /// Refresh via POST /auth/native/refresh. The password is never stored.
    /// </summary>
    public class NativeAuthFlow
    {
        private readonly string _baseUrl;
        private readonly HttpClient _http;

        public NativeAuthFlow(string baseUrl)
        {
            _baseUrl = UrlUtil.NormalizeBase(baseUrl);
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        private async Task<JObject> PostAsync(string path, JObject body)
        {
            var url = _baseUrl + path;
            using (var content = new StringContent((body ?? new JObject()).ToString(), Encoding.UTF8, "application/json"))
            using (var resp = await _http.PostAsync(url, content).ConfigureAwait(false))
            {
                var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException("POST " + path + " -> " + (int)resp.StatusCode + ": " + text);
                return JObject.Parse(text);
            }
        }

        /// <summary>Step 1+2: authorize then password-login. Returns provider response (never stores password).</summary>
        public async Task<JObject> AuthorizeWithPasswordAsync(string username, string password)
        {
            var authz = await PostAsync("/auth/native/authorize",
                new JObject { ["username"] = username }).ConfigureAwait(false);
            var loginBody = new JObject
            {
                ["username"] = username,
                ["password"] = password
            };
            // Carry any device/session fields the authorize step returned.
            foreach (var p in authz.Properties())
            {
                if (p.Name == "device_code" || p.Name == "session" || p.Name == "request_id")
                    loginBody[p.Name] = p.Value;
            }
            var login = await PostAsync("/auth/password-login", loginBody).ConfigureAwait(false);
            login["__authorize"] = authz;
            return login;
        }

        /// <summary>Step 3: exchange the login grant for tokens.</summary>
        public async Task<TokenPair> ExchangeTokenAsync(JObject loginResult)
        {
            var body = new JObject();
            var authz = loginResult["__authorize"] as JObject;
            if (authz != null)
                foreach (var p in authz.Properties())
                    body[p.Name] = p.Value;
            foreach (var p in loginResult.Properties())
            {
                if (p.Name == "__authorize") continue;
                body[p.Name] = p.Value;
            }
            var tok = await PostAsync("/auth/native/token", body).ConfigureAwait(false);
            return ExtractTokens(tok);
        }

        /// <summary>Refresh an access token. Returns null when the refresh token is dead.</summary>
        public async Task<TokenPair> RefreshAsync(string refreshToken)
        {
            try
            {
                var tok = await PostAsync("/auth/native/refresh",
                    new JObject { ["refresh_token"] = refreshToken }).ConfigureAwait(false);
                return ExtractTokens(tok);
            }
            catch (Exception ex)
            {
                Log.Info("Native refresh failed: " + ex.Message);
                return null;
            }
        }

        public static TokenPair ExtractTokens(JObject tok)
        {
            var pair = new TokenPair { Raw = tok ?? new JObject() };
            pair.AccessToken =
                Str(tok, "access_token") ?? Str(tok, "accessToken") ?? Str(tok, "token") ?? "";
            pair.RefreshToken =
                Str(tok, "refresh_token") ?? Str(tok, "refreshToken") ?? "";
            return pair;
        }

        private static string Str(JObject o, string name)
        {
            var t = o?[name];
            return t != null && t.Type == JTokenType.String ? t.Value<string>() : null;
        }
    }
}
