using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MedicAIGUI.Services;

namespace MedicAIGUI.Views
{
    public partial class PriorityPlayersView : UserControl
    {
        private readonly MedicBotService _service = MedicBotService.Instance;

        public PriorityPlayersView()
        {
            InitializeComponent();
            Loaded += (s, e) => LoadTarget();
        }

        private void LoadTarget()
        {
            TargetNameBox.Text = _service.Settings.CurrentTarget;
        }

        private void ClearTargetBtn_Click(object sender, RoutedEventArgs e)
        {
            TargetNameBox.Clear();
        }

        private async void SyncBtn_Click(object sender, RoutedEventArgs e)
        {
            _service.Settings.CurrentTarget = TargetNameBox.Text.Trim();
            await _service.SendConfigUpdate(new() { ["current_target"] = _service.Settings.CurrentTarget });
            _service.Settings.SaveSettings();
            MessageBox.Show("Target saved and synced to bot.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void TargetNameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SyncBtn_Click(sender, e);
            }
        }
    }
}