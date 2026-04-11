using System;
using System.Windows;
using System.Windows.Controls;
using MedicAIGUI.Services;

namespace MedicAIGUI.Views
{
    public partial class TuningView : UserControl
    {
        private readonly MedicBotService _service = MedicBotService.Instance;
        private SavedSettings _settings;

        public TuningView()
        {
            InitializeComponent();
            _settings = _service.Settings;
            DataContext = _settings;
        }

        private void ProfileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProfileSelector.SelectedIndex == -1) return;

            string profile = (ProfileSelector.SelectedItem as ComboBoxItem)?.Content.ToString();
            
            // Ultra-precise professional tuning profiles
            if (profile.Contains("Balanced"))
            {
                _settings.Smoothing = 0.12;
                _settings.FollowBackThresh = 180;
                _settings.FollowFwdThresh = 400;
            }
            else if (profile.Contains("Aggressive"))
            {
                _settings.Smoothing = 0.05;
                _settings.FollowBackThresh = 80;
                _settings.FollowFwdThresh = 250;
            }
            else if (profile.Contains("Defensive"))
            {
                _settings.Smoothing = 0.20;
                _settings.FollowBackThresh = 250;
                _settings.FollowFwdThresh = 600;
            }
            else if (profile.Contains("Stealth"))
            {
                _settings.Smoothing = 0.15;
                _settings.FollowBackThresh = 300;
                _settings.FollowFwdThresh = 800;
            }
            
            _settings.SaveSettings();
        }

        private async void Sync_Click(object sender, RoutedEventArgs e)
        {
            await _service.SyncConfigAsync();
            MessageBox.Show("Neural Profile Uplinked to Node.", "Sync Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
