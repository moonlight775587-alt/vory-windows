using System;
using System.Collections.Generic;

namespace VoryWindows.Models
{
    public enum AuthMode
    {
        SessionToken = 0,
        UsernamePassword = 1,
        BrowserOidc = 2
    }

    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        Failed
    }

    /// <summary>One saved gateway. Tokens live here; the whole object is DPAPI-encrypted on disk.</summary>
    public class GatewayCredential
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public AuthMode AuthMode { get; set; } = AuthMode.SessionToken;
        public string SessionToken { get; set; } = "";
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string CfAccessId { get; set; } = "";
        public string CfAccessSecret { get; set; } = "";
        public string SelectedProfile { get; set; } = "default";
        public Dictionary<string, string> BotColors { get; set; } = new Dictionary<string, string>();
        public HashSet<string> PinnedSessions { get; set; } = new HashSet<string>();
        public HashSet<string> ArchivedSessions { get; set; } = new HashSet<string>();

        public bool UsesBearer
        {
            get { return AuthMode == AuthMode.UsernamePassword || AuthMode == AuthMode.BrowserOidc; }
        }

        public bool HasCfAccess
        {
            get { return !string.IsNullOrWhiteSpace(CfAccessId) && !string.IsNullOrWhiteSpace(CfAccessSecret); }
        }
    }

    public class ChatSession
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "Untitled";
        public string Profile { get; set; } = "";
        public string Project { get; set; } = "";
        public DateTime UpdatedAt { get; set; } = DateTime.MinValue;
        public string Preview { get; set; } = "";
        public bool NeedsYou { get; set; }
    }

    public class ProfileInfo
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Model { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public class ModelOption
    {
        public string Provider { get; set; } = "";
        public string Slug { get; set; } = "";
        public string Label { get; set; } = "";
    }

    public class CronJob
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Schedule { get; set; } = "";
        public bool Paused { get; set; }
        public string LastRun { get; set; } = "";
    }

    public class FileItem
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public string Modified { get; set; } = "";
    }

    public class PendingApproval
    {
        public string RequestId { get; set; } = "";
        public string SessionId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Command { get; set; } = "";
        public string Detail { get; set; } = "";
        public bool Queued { get; set; }
        public DateTime ReceivedAt { get; set; } = DateTime.Now;
    }

    public class PendingPrompt
    {
        public string RequestId { get; set; } = "";
        public string SessionId { get; set; } = "";
        public string Kind { get; set; } = ""; // clarify | sudo | secret | vault
        public string Title { get; set; } = "";
        public string Prompt { get; set; } = "";
        public bool IsSecret { get; set; }
    }

    public static class UrlUtil
    {
        public static string NormalizeBase(string raw)
        {
            var s = (raw ?? "").Trim().TrimEnd('/');
            var idx = s.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
            if (idx > 0) s = s.Substring(0, idx);
            else if (s.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            return s.TrimEnd('/');
        }

        public static string ToWsUrl(string httpBase, string pathAndQuery)
        {
            string scheme = httpBase.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
            string rest = httpBase;
            int p = rest.IndexOf("://", StringComparison.Ordinal);
            if (p >= 0) rest = rest.Substring(p + 3);
            return scheme + "://" + rest.TrimEnd('/') + pathAndQuery;
        }

        public static bool IsPrivateHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            host = host.Trim().ToLowerInvariant();
            if (host == "localhost" || host == "127.0.0.1" || host == "::1" || host.EndsWith(".local")) return true;
            System.Net.IPAddress ip;
            if (System.Net.IPAddress.TryParse(host, out ip))
            {
                var b = ip.GetAddressBytes();
                if (b.Length == 4)
                {
                    if (b[0] == 10) return true;
                    if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
                    if (b[0] == 192 && b[1] == 168) return true;
                    if (b[0] == 169 && b[1] == 254) return true;
                }
                else if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
            }
            return false;
        }

        public static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // Never let a pasted token leak into logs: mask long base64-ish runs.
            return System.Text.RegularExpressions.Regex.Replace(
                text,
                @"[A-Za-z0-9_\-+/=]{24,}",
                m => m.Value.Length <= 28 ? m.Value : m.Value.Substring(0, 6) + "…" + m.Value.Substring(m.Value.Length - 4));
        }
    }
}
