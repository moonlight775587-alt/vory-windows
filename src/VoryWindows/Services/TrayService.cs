using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using VoryWindows.Models;

namespace VoryWindows.Services
{
    /// <summary>
    /// System tray icon: running-turn / approval menu, balloon notifications for approvals,
    /// "Open Vory" to restore the window (Mac menu-bar style behavior).
    /// </summary>
    public class TrayService : IDisposable
    {
        private readonly AppState _state;
        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu;
        private readonly List<PendingApproval> _pending = new List<PendingApproval>();
        private MainWindow _window;

        public TrayService(AppState state)
        {
            _state = state;
            _menu = new ContextMenuStrip();
            _menu.Items.Add("Open Vory", null, (s, e) => ShowWindow());
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("Exit", null, (s, e) => ExitApp());

            _icon = new NotifyIcon
            {
                Text = "Vory",
                Icon = BuildIcon(),
                ContextMenuStrip = _menu,
                Visible = true
            };
            _icon.DoubleClick += (s, e) => ShowWindow();

            _state.ApprovalRequested += OnApproval;
        }

        public void AttachWindow(MainWindow window)
        {
            _window = window;
        }

        private static Icon BuildIcon()
        {
            // Drawn "V" mark so no icon file is needed.
            using (var bmp = new Bitmap(64, 64))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(20, 20, 22));
                using (var pen = new Pen(Color.FromArgb(91, 141, 239), 9f))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawLine(pen, 14, 14, 32, 52);
                    g.DrawLine(pen, 50, 14, 32, 52);
                }
                var h = bmp.GetHicon();
                return Icon.FromHandle(h);
            }
        }

        private void OnApproval(object sender, PendingApproval ap)
        {
            lock (_pending) { _pending.Add(ap); }
            RefreshMenu();
            try
            {
                _icon.BalloonTipTitle = "Approval needed - " + ap.Title;
                _icon.BalloonTipText = string.IsNullOrEmpty(ap.Command) ? ap.Detail : ap.Command;
                _icon.ShowBalloonTip(8000);
            }
            catch { }
        }

        private void RefreshMenu()
        {
            if (_menu.InvokeRequired)
            {
                _menu.BeginInvoke(new Action(RefreshMenu));
                return;
            }
            // Rebuild: keep "Open Vory", separator, dynamic approvals, separator, Exit.
            while (_menu.Items.Count > 2) _menu.Items.RemoveAt(2);
            List<PendingApproval> copy;
            lock (_pending) { copy = new List<PendingApproval>(_pending); }
            if (copy.Count > 0)
            {
                _menu.Items.Add(new ToolStripSeparator());
                foreach (var ap in copy)
                {
                    var item = new ToolStripMenuItem("Approval: " + Trim(ap.Title, 32));
                    item.DropDownItems.Add("Approve once", null, async (s, e) => await AnswerAndDrop(ap, "once"));
                    item.DropDownItems.Add("Approve for session", null, async (s, e) => await AnswerAndDrop(ap, "session"));
                    item.DropDownItems.Add("Always allow", null, async (s, e) => await AnswerAndDrop(ap, "always"));
                    item.DropDownItems.Add("Deny", null, async (s, e) => await AnswerAndDrop(ap, "deny"));
                    _menu.Items.Add(item);
                }
            }
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("Exit", null, (s, e) => ExitApp());
        }

        private async System.Threading.Tasks.Task AnswerAndDrop(PendingApproval ap, string choice)
        {
            try { await _state.AnswerApprovalAsync(ap, choice); }
            catch (Exception ex) { Log.Warn("Tray approval answer failed: " + ex.Message); }
            lock (_pending) { _pending.Remove(ap); }
            RefreshMenu();
        }

        private static string Trim(string s, int n)
        {
            s = s ?? "";
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        public void ShowWindow()
        {
            if (_window == null) return;
            _window.Dispatcher.BeginInvoke(new Action(() =>
            {
                _window.Show();
                _window.WindowState = System.Windows.WindowState.Normal;
                _window.Activate();
            }));
        }

        public void NotifyTurnComplete(string botName, string chatTitle)
        {
            try
            {
                _icon.BalloonTipTitle = botName;
                _icon.BalloonTipText = "Finished reply in " + chatTitle;
                _icon.ShowBalloonTip(4000);
            }
            catch { }
        }

        private void ExitApp()
        {
            _window?.Dispatcher.BeginInvoke(new Action(() =>
            {
                _window.ForceClose = true;
                _window.Close();
            }));
        }

        public void Dispose()
        {
            try
            {
                _state.ApprovalRequested -= OnApproval;
                _icon.Visible = false;
                _icon.Dispose();
                _menu.Dispose();
            }
            catch { }
        }
    }
}
