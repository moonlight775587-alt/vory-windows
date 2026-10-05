using System;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json.Linq;
using VoryWindows.Models;
using VoryWindows.Services;

namespace VoryWindows.Views.Dialogs
{
    public partial class ProfileEditorWindow : Window
    {
        private readonly AppState _state;
        private readonly ProfileInfo _profile;

        public ProfileEditorWindow(AppState state, ProfileInfo profile)
        {
            InitializeComponent();
            _state = state;
            _profile = profile;
            TitleText.Text = "Edit bot — " + profile.Name;
            DescriptionBox.Text = profile.Description;
            ModelBox.Text = profile.Model;
            LoadSoulAsync();
        }

        private async void LoadSoulAsync()
        {
            try
            {
                var tok = await _state.Rest.GetAsync(
                    "/api/profiles/" + Uri.EscapeDataString(_profile.Name) + "/soul");
                SoulBox.Text = (tok as JObject)?["soul"]?.ToString() ?? tok.ToString();
            }
            catch (Exception ex) { StatusText.Text = "SOUL.md: " + ex.Message; }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Saving…";
            try
            {
                var name = Uri.EscapeDataString(_profile.Name);
                await _state.Rest.PutJsonAsync("/api/profiles/" + name + "/description",
                    new JObject { ["description"] = DescriptionBox.Text });
                await _state.Rest.PutJsonAsync("/api/profiles/" + name + "/model",
                    new JObject { ["model"] = ModelBox.Text });
                await _state.Rest.PutJsonAsync("/api/profiles/" + name + "/soul",
                    new JObject { ["soul"] = SoulBox.Text });
                DialogResult = true;
                Close();
            }
            catch (Exception ex) { StatusText.Text = "Save failed: " + ex.Message; }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
