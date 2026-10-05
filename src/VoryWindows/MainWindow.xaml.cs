using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using VoryWindows.Models;
using VoryWindows.Services;
using VoryWindows.ViewModels;
using VoryWindows.Views.Dialogs;

namespace VoryWindows
{
    public partial class MainWindow : Window
    {
        private readonly AppState _state;

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
            UpdateConnDot();
            Loaded += (s, e) =>
            {
                if (_state.Gateways.Count == 0)
                {
                    var dlg = new GatewayDialog(_state, null) { Owner = this };
                    dlg.ShowDialog();
                }
            };
        }

        private void UpdateConnDot()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)); // red
            switch (_state.ConnectionState)
            {
                case ConnectionState.Connected:
                    brush = new SolidColorBrush(Color.FromRgb(0x46, 0xA7, 0x58)); break;
                case ConnectionState.Connecting:
                case ConnectionState.Reconnecting:
                    brush = new SolidColorBrush(Color.FromRgb(0xE8, 0xA1, 0x3C)); break;
            }
            ConnDot.Fill = brush;
            ConnDot.ToolTip = "Connection: " + _state.ConnectionState;
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
