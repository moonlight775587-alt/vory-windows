using System;
using System.IO;
using System.Text;
using VoryWindows.Models;

namespace VoryWindows.Services
{
    /// <summary>Redacted file log. Token-shaped runs are masked before writing.</summary>
    public static class Log
    {
        private static readonly object _gate = new object();
        private static readonly string _path;

        static Log()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VoryWindows", "logs");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "app.log");
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        private static void Write(string level, string message)
        {
            try
            {
                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] "
                    + UrlUtil.Redact(message ?? "") + Environment.NewLine;
                lock (_gate)
                {
                    File.AppendAllText(_path, line, Encoding.UTF8);
                }
            }
            catch { /* logging must never crash the app */ }
        }

        public static string Tail(int maxChars)
        {
            try
            {
                lock (_gate)
                {
                    if (!File.Exists(_path)) return "";
                    var text = File.ReadAllText(_path, Encoding.UTF8);
                    return text.Length <= maxChars ? text : text.Substring(text.Length - maxChars);
                }
            }
            catch { return ""; }
        }

        public static string LogPath { get { return _path; } }
    }
}
