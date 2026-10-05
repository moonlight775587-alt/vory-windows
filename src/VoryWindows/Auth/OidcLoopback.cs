using System;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using VoryWindows.Services;

namespace VoryWindows.Auth
{
    /// <summary>
    /// PKCE + loopback redirect listener for "Sign in with browser" (Nous Portal / OIDC).
    /// Listens on 127.0.0.1 with an ephemeral port, opens the system browser, captures
    /// the authorization code, then exchanges it at POST /auth/native/token.
    /// If the listener cannot bind, the caller falls back to manual code paste.
    /// </summary>
    public class OidcLoopback
    {
        public class Result
        {
            public bool Ok;
            public string Code = "";
            public string CodeVerifier = "";
            public string Error = "";
        }

        public static string NewCodeVerifier()
        {
            var bytes = new byte[48];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return Base64Url(bytes);
        }

        public static string CodeChallenge(string verifier)
        {
            using (var sha = SHA256.Create())
                return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }

        private static string Base64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// Opens authorizeUrl (with {PORT} and {CHALLENGE} placeholders replaced) and waits
        /// for the loopback callback carrying ?code=. Times out after 5 minutes.
        /// </summary>
        public static async Task<Result> CaptureCodeAsync(string authorizeUrlTemplate, string codeVerifier)
        {
            var result = new Result { CodeVerifier = codeVerifier };
            HttpListener listener = null;
            try
            {
                listener = new HttpListener();
                // Ephemeral port: probe a free one first.
                var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                tcp.Start();
                int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
                tcp.Stop();

                string prefix = "http://127.0.0.1:" + port + "/callback/";
                listener.Prefixes.Add(prefix);
                listener.Start();
            }
            catch (Exception ex)
            {
                result.Error = "Could not start loopback listener: " + ex.Message;
                return result;
            }

            string challenge = CodeChallenge(codeVerifier);
            string url = authorizeUrlTemplate
                .Replace("{PORT}", port.ToString())
                .Replace("{CHALLENGE}", Uri.EscapeDataString(challenge));

            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                result.Error = "Could not open browser: " + ex.Message;
                try { listener.Stop(); } catch { }
                return result;
            }

            try
            {
                var getCtx = listener.GetContextAsync();
                var timeout = Task.Delay(TimeSpan.FromMinutes(5));
                var done = await Task.WhenAny(getCtx, timeout).ConfigureAwait(false);
                if (done == timeout)
                {
                    result.Error = "Timed out waiting for the browser sign-in.";
                    return result;
                }
                var ctx = await getCtx.ConfigureAwait(false);
                try
                {
                    string code = ctx.Request.QueryString["code"] ?? "";
                    string err = ctx.Request.QueryString["error"] ?? "";
                    string html = string.IsNullOrEmpty(err)
                        ? "<html><body style='font-family:sans-serif'><h2>Signed in</h2><p>You can close this tab and return to Vory.</p></body></html>"
                        : "<html><body style='font-family:sans-serif'><h2>Sign-in failed</h2><p>" + WebUtility.HtmlEncode(err) + "</p></body></html>";
                    var bytes = Encoding.UTF8.GetBytes(html);
                    ctx.Response.ContentType = "text/html";
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    ctx.Response.OutputStream.Close();
                    if (!string.IsNullOrEmpty(err)) { result.Error = err; return result; }
                    result.Ok = true;
                    result.Code = code;
                    return result;
                }
                finally { try { ctx.Response.Close(); } catch { } }
            }
            finally { try { listener.Stop(); } catch { } }
        }

        /// <summary>Exchanges an authorization code for tokens at POST /auth/native/token.</summary>
        public static async Task<TokenPair> ExchangeCodeAsync(string baseUrl, string code, string codeVerifier)
        {
            var flow = new NativeAuthFlow(baseUrl);
            // NativeAuthFlow.ExchangeTokenAsync posts the object to /auth/native/token.
            var login = new JObject
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["code_verifier"] = codeVerifier
            };
            return await flow.ExchangeTokenAsync(login).ConfigureAwait(false);
        }
    }
}
