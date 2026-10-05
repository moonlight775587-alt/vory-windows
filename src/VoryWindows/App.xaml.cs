using System;
using System.Net;
using System.Windows;
using VoryWindows.Services;

namespace VoryWindows
{
    public partial class App : Application
    {
        public static AppState State { get; private set; }
        public static TrayService Tray { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // TLS 1.2+ for HTTPS gateways (Server 2016 defaults may exclude it)
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Log.Info("Vory for Windows starting");

            State = new AppState();
            Tray = new TrayService(State);

            var main = new MainWindow(State);
            main.Closed += (s, args) =>
            {
                Tray.Dispose();
                Log.Info("Vory for Windows exiting");
                Shutdown();
            };
            main.Show();
        }
    }
}
