using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MedicAIGUI.Services;

namespace MedicAIGUI.Views
{
    public partial class SettingsView : UserControl
    {
        private readonly MedicBotService _service = MedicBotService.Instance;
        private SavedSettings _settings;

        public SettingsView()
        {
            InitializeComponent();
            _settings = _service.Settings;
            DataContext = _settings;
        }

        private async void SyncAll_Click(object sender, RoutedEventArgs e)
        {
            _settings.SaveSettings();
            await _service.SyncConfigAsync();
            MessageBox.Show("All configurations synchronized to the AI Node.", "Sync Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void PushConfigBtn_Click(object sender, RoutedEventArgs e)
        {
            _settings.SaveSettings();
            bool success = await _service.SyncBrainConfigAsync();
            if (success) 
            {
                MessageBox.Show("Brain configuration synchronized.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Failed to sync configuration.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            _service.LoadLocalSettings();
            _settings = _service.Settings;
            DataContext = _settings;
        }

        private void UpdateAccent_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            string colorHex = btn.Tag?.ToString();
            if (string.IsNullOrEmpty(colorHex)) return;

            _settings.AccentColor = colorHex;
            ApplyAccentColor(colorHex);
        }

        private void ApplyAccentColor(string hex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                Application.Current.Resources["AccentCool"] = color;
                
                // Update based on dynamic resource
                var brush = new SolidColorBrush(color);
                Application.Current.Resources["AccentCoolBrush"] = brush;
            }
            catch { }
        }

        // Additional handlers for font, opacity, etc. can be added here
        // or handled via binding if MainWindow reacts to PropertyChanged
    }
}
