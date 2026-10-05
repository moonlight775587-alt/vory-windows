using System.Windows;

namespace VoryWindows.Views.Dialogs
{
    public partial class CronJobDialog : Window
    {
        public string JobName { get { return NameBox.Text.Trim(); } }
        public string JobSchedule { get { return ScheduleBox.Text.Trim(); } }
        public string JobCommand { get { return CommandBox.Text; } }

        public CronJobDialog()
        {
            InitializeComponent();
            NameBox.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(JobName) || string.IsNullOrWhiteSpace(JobSchedule)) return;
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
