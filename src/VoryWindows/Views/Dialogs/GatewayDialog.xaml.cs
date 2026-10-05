using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using VoryWindows.Auth;
using VoryWindows.Models;
using VoryWindows.Services;

namespace VoryWindows.Views.Dialogs
{
    public partial class GatewayDialog : Window
    {
        private readonly AppState _state;
        private readonly GatewayCredential _editing; // null = new
        private bool _tested;
        private string _oidcVerifier = "";
        private TokenPair _oidcTokens;

        public GatewayDialog(AppState state, GatewayCredential editing)
        {
            InitializeComponent();
            _state = state;
            _editing = editing;
            if (editing != null)
            {
                NameBox.Text = editing.Name;
                UrlBox.Text = editing.BaseUrl;
                AuthModeBox.SelectedIndex = (int)editing.AuthMode;
                CfIdBox.Text = editing.CfAccessId;
                // Secrets are not shown back; user re-enters if changing.
            }
            else
            {
                AuthModeBox.SelectedIndex = 0;
            }
            UrlBox.TextChanged += (s, e) => UpdateLanWarning();
            UpdateLanWarning();
        }

        private void UpdateLanWarning()
        {
            try
            {
                var uri = new Uri(UrlUtil.NormalizeBase(UrlBox.Text));
                LanWarning.Visibility =
                    uri.Scheme == "http" && !UrlUtil.IsPrivateHost(uri.Host)
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { LanWarning.Visibility = Visibility.Collapsed; }
        }

        private void AuthModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = AuthModeBox.SelectedIndex;
            TokenPanel.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
            UserPassPanel.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
            OidcPanel.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        private GatewayCredential BuildCandidate()
        {
            var g = _editing ?? new GatewayCredential();
            g.Name = NameBox.Text.Trim();
            g.BaseUrl = UrlUtil.NormalizeBase(UrlBox.Text);
            g.AuthMode = (AuthMode)AuthModeBox.SelectedIndex;
            if (g.AuthMode == AuthMode.SessionToken && !string.IsNullOrEmpty(TokenBox.Password))
                g.SessionToken = TokenBox.Password;
            if (!string.IsNullOrEmpty(CfIdBox.Text)) g.CfAccessId = CfIdBox.Text.Trim();
            if (!string.IsNullOrEmpty(CfSecretBox.Password)) g.CfAccessSecret = CfSecretBox.Password;
            if (g.AuthMode == AuthMode.BrowserOidc && _oidcTokens != null)
            {
                g.AccessToken = _oidcTokens.AccessToken;
                g.RefreshToken = _oidcTokens.RefreshToken;
            }
            return g;
        }

        private async void Test_Click(object sender, RoutedEventArgs e)
        {
            var g = BuildCandidate();
            if (string.IsNullOrWhiteSpace(g.Name) || string.IsNullOrWhiteSpace(g.BaseUrl))
            {
                LegResults.ItemsSource = new[]
                {
                    new TestLegResult { Name = "Input", Passed = false, Detail = "Name and URL are required." }
                };
                return;
            }

            // Username/password: run the native flow now (password never stored).
            if (g.AuthMode == AuthMode.UsernamePassword)
            {
                try
                {
                    var flow = new NativeAuthFlow(g.BaseUrl);
                    var login = await flow.AuthorizeWithPasswordAsync(UsernameBox.Text.Trim(), PasswordBox.Password);
                    var pair = await flow.ExchangeTokenAsync(login);
                    g.AccessToken = pair.AccessToken;
                    g.RefreshToken = pair.RefreshToken;
                    PasswordBox.Clear();
                }
                catch (Exception ex)
                {
                    LegResults.ItemsSource = new[]
                    {
                        new TestLegResult { Name = "Sign in", Passed = false, Detail = ex.Message }
                    };
                    return;
                }
            }

            if (g.AuthMode == AuthMode.BrowserOidc && _oidcTokens == null)
            {
                LegResults.ItemsSource = new[]
                {
                    new TestLegResult { Name = "Sign in", Passed = false, Detail = "Complete the browser sign-in first." }
                };
                return;
            }

            SaveButton.IsEnabled = false;
            LegResults.ItemsSource = new[] { new TestLegResult { Name = "Testing…", Passed = true, Detail = "" } };
            var legs = await _state.TestGatewayAsync(g);
            LegResults.ItemsSource = legs;
            _tested = legs.All(l => l.Passed);
            SaveButton.IsEnabled = _tested;
            if (_tested)
            {
                // Keep the fresh tokens on the candidate for Save.
                _pendingSave = g;
            }
        }

        private GatewayCredential _pendingSave;

        private async void OidcSignIn_Click(object sender, RoutedEventArgs e)
        {
            var template = AuthorizeUrlBox.Text.Trim();
            if (string.IsNullOrEmpty(template))
            {
                OidcStatus.Text = "Enter the authorize URL template first.";
                return;
            }
            _oidcVerifier = OidcLoopback.NewCodeVerifier();
            OidcStatus.Text = "Waiting for the browser sign-in…";
            var res = await OidcLoopback.CaptureCodeAsync(template, _oidcVerifier);
            if (!res.Ok)
            {
                OidcStatus.Text = string.IsNullOrEmpty(res.Error)
                    ? "Falling back to manual code paste."
                    : "Loopback failed: " + res.Error + " — paste the code manually.";
                return;
            }
            await ExchangeOidcCodeAsync(res.Code);
        }

        private async void ManualCode_Click(object sender, RoutedEventArgs e)
        {
            var code = ManualCodeBox.Text.Trim();
            if (string.IsNullOrEmpty(code)) return;
            if (string.IsNullOrEmpty(_oidcVerifier)) _oidcVerifier = OidcLoopback.NewCodeVerifier();
            await ExchangeOidcCodeAsync(code);
        }

        private async Task ExchangeOidcCodeAsync(string code)
        {
            try
            {
                _oidcTokens = await OidcLoopback.ExchangeCodeAsync(
                    UrlUtil.NormalizeBase(UrlBox.Text), code, _oidcVerifier);
                OidcStatus.Text = "Signed in. Now run Test connection.";
            }
            catch (Exception ex)
            {
                OidcStatus.Text = "Token exchange failed: " + ex.Message;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var g = _pendingSave ?? BuildCandidate();
            if (_editing == null)
            {
                _state.Gateways.Add(g);
                _state.ActiveGateway = g;
            }
            // When editing, BuildCandidate already mutated _editing in place.
            _state.SaveGateways();
            Task.Run(async () =>
            {
                await _state.DisconnectAsync();
                await _state.ConnectAsync();
            });
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
