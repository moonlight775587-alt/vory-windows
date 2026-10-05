using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VoryWindows.Models;
using VoryWindows.Services;
using VoryWindows.ViewModels;
using VoryWindows.Views.Dialogs;

namespace VoryWindows
{
    public partial class MainWindow : Window
    {
        private readonly AppState _state;
        private Storyboard _pulse;

        public bool ForceClose { get; set; }

        public MainWindow(AppState state)
        {
            InitializeComponent();
            _state = state;
            DataContext = new MainViewModel(state);
            App.Tray.AttachWindow(this);
            _state.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AppState.ConnectionState))
                    Dispatcher.BeginInvoke(new Action(UpdateConnDot));
            };
            StateChanged += (s, e) => UpdateChromeForState();
            UpdateConnDot();
            UpdateChromeForState();
            Loaded += (s, e) =>
            {
                _pulse = (Storyboard)FindResource("OrbPulse");
                if (_state.Gateways.Count == 0)
                {
                    var dlg = new GatewayDialog(_state, null) { Owner = this };
                    dlg.ShowDialog();
                }
            };
        }

        private void UpdateConnDot()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0xF0, 0x44, 0x52)); // red
            var label = "Offline";
            var busy = false;
            switch (_state.ConnectionState)
            {
                case ConnectionState.Connected:
                    brush = new SolidColorBrush(Color.FromRgb(0x17, 0xC9, 0x64));
                    label = "Online";
                    break;
                case ConnectionState.Connecting:
                case ConnectionState.Reconnecting:
                    brush = new SolidColorBrush(Color.FromRgb(0xF5, 0xA5, 0x24));
                    label = "Connecting…";
                    busy = true;
                    break;
            }
            ConnDot.Fill = brush;
            ConnLabel.Text = label;
            if (_pulse != null)
            {
                if (busy)
                {
                    Storyboard.SetTarget(_pulse, ConnHalo);
                    _pulse.Begin();
                }
                else
                {
                    _pulse.Stop();
                    ConnHalo.Opacity = 0;
                }
            }
        }

        // ---------------- custom window chrome ----------------

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void MinButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaxButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close(); // OnClosing routes this to the tray unless ForceClose.
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void UpdateChromeForState()
        {
            if (WindowState == WindowState.Maximized)
            {
                ChromeBorder.CornerRadius = new CornerRadius(0);
                ChromeBorder.BorderThickness = new Thickness(0);
                MaxButton.Content = "❐";
                MaxButton.ToolTip = "Restore";
            }
            else
            {
                ChromeBorder.CornerRadius = new CornerRadius(14);
                ChromeBorder.BorderThickness = new Thickness(1);
                MaxButton.Content = "▢";
                MaxButton.ToolTip = "Maximize";
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hs = (HwndSource)PresentationSource.FromVisual(this);
            if (hs != null)
                hs.AddHook(WndProc);
        }

        private const int WM_NCHITTEST = 0x84;
        private const int WM_GETMINMAXINFO = 0x24;
        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST && WindowState == WindowState.Normal)
            {
                int raw = lParam.ToInt32();
                int x = raw & 0xFFFF;
                int y = (raw >> 16) & 0xFFFF;
                var p = PointFromScreen(new Point(x, y));
                const int grip = 8;
                bool left = p.X <= grip;
                bool right = p.X >= ActualWidth - grip;
                bool top = p.Y <= grip;
                bool bottom = p.Y >= ActualHeight - grip;
                int hit = HTCLIENT;
                if (top && left) hit = HTTOPLEFT;
                else if (top && right) hit = HTTOPRIGHT;
                else if (bottom && left) hit = HTBOTTOMLEFT;
                else if (bottom && right) hit = HTBOTTOMRIGHT;
                else if (left) hit = HTLEFT;
                else if (right) hit = HTRIGHT;
                else if (top) hit = HTTOP;
                else if (bottom) hit = HTBOTTOM;
                if (hit != HTCLIENT)
                {
                    handled = true;
                    return new IntPtr(hit);
                }
            }
            else if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left; public int top; public int right; public int bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MONITORINFO
        {
            public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

        private void WmGetMinMaxInfo(IntPtr lParam)
        {
            var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
            IntPtr monitor = MonitorFromWindow(new WindowInteropHelper(this).Handle, 2);
            if (monitor != IntPtr.Zero)
            {
                var mi = new MONITORINFO();
                if (GetMonitorInfo(monitor, mi))
                {
                    mmi.ptMaxPosition.x = Math.Abs(mi.rcWork.left - mi.rcMonitor.left);
                    mmi.ptMaxPosition.y = Math.Abs(mi.rcWork.top - mi.rcMonitor.top);
                    mmi.ptMaxSize.x = Math.Abs(mi.rcWork.right - mi.rcWork.left);
                    mmi.ptMaxSize.y = Math.Abs(mi.rcWork.bottom - mi.rcWork.top);
                }
            }
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!ForceClose)
            {
                // Minimize to tray like Vory for Mac's menu-bar behavior.
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnClosing(e);
        }
    }
}
