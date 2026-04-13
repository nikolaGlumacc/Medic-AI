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
        private bool _loaded;

        public TuningView()
        {
            InitializeComponent();
            _settings = _service.Settings;
            DataContext = _settings;
            Loaded += TuningView_Loaded;
        }

        private void TuningView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_loaded) return;
            _loaded = true;

            // Wire up spy watch checkbox from current setting
            CbSpyWatch.IsChecked = _settings.SpyCheckFrequency > 0;

            // Select profile combo based on current smoothing value
            if      (_settings.Smoothing <= 0.06)  ProfileSelector.SelectedIndex = 1; // Aggressive
            else if (_settings.Smoothing >= 0.18)  ProfileSelector.SelectedIndex = 2; // Defensive
            else if (_settings.FollowFwdThresh > 600) ProfileSelector.SelectedIndex = 3; // Stealth
            else                                    ProfileSelector.SelectedIndex = 0; // Balanced
        }

        private void CbSpyWatch_Changed(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            // When spy watch is disabled, set frequency to 0 (disabled)
            // When enabled, restore to a reasonable default if currently 0
            if (CbSpyWatch.IsChecked == true && _settings.SpyCheckFrequency == 0)
                _settings.SpyCheckFrequency = 8;
            else if (CbSpyWatch.IsChecked == false)
                _settings.SpyCheckFrequency = 0;
        }

        private void ProfileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;

            var profile = (ProfileSelector.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrWhiteSpace(profile)) return;

            if (profile.Contains("Balanced"))
            {
                _settings.Smoothing         = 0.12;
                _settings.FollowBackThresh  = 80;
                _settings.FollowFwdThresh   = 180;
                _settings.RetreatHealthThreshold = 50;
                _settings.UberPopThreshold  = 95;
            }
            else if (profile.Contains("Aggressive"))
            {
                _settings.Smoothing         = 0.05;
                _settings.FollowBackThresh  = 60;
                _settings.FollowFwdThresh   = 120;
                _settings.RetreatHealthThreshold = 30;
                _settings.UberPopThreshold  = 80;
            }
            else if (profile.Contains("Defensive"))
            {
                _settings.Smoothing         = 0.20;
                _settings.FollowBackThresh  = 150;
                _settings.FollowFwdThresh   = 400;
                _settings.RetreatHealthThreshold = 80;
                _settings.UberPopThreshold  = 98;
            }
            else if (profile.Contains("Stealth"))
            {
                _settings.Smoothing         = 0.15;
                _settings.FollowBackThresh  = 200;
                _settings.FollowFwdThresh   = 700;
                _settings.RetreatHealthThreshold = 60;
                _settings.UberPopThreshold  = 99;
            }

            // Refresh bindings
            DataContext = null;
            DataContext = _settings;

            // Re-set spy watch (not a bound property)
            CbSpyWatch.IsChecked = _settings.SpyCheckFrequency > 0;
        }

        private async void Sync_Click(object sender, RoutedEventArgs e)
        {
            _settings.SaveSettings();
            _service.ApplyConnectionSettings(_settings);
            var ok = await _service.SyncConfigAsync();

            MessageBox.Show(
                ok ? "Tuning profile synced to bot successfully." : "Sync failed — is the bot server running?",
                ok ? "Sync OK" : "Sync Failed",
                MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning
            );
        }
    }
}