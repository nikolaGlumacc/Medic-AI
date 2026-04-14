using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MedicAIGUI.Services;

namespace MedicAIGUI.Views
{
    public partial class SettingsView : UserControl
    {
        private readonly MedicBotService _service = MedicBotService.Instance;
        private SavedSettings _settings;
        private bool _eventsAttached;
        private bool _suspendDirtyTracking;

        public SettingsView()
        {
            InitializeComponent();

            _settings = _service.Settings;
            DataContext = _settings;
            _service.ApplyConnectionSettings(_settings);
            Loaded += SettingsView_Loaded;
            Unloaded += SettingsView_Unloaded;
        }

        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_eventsAttached)
            {
                return;
            }

            _settings.PropertyChanged += Settings_PropertyChanged;
            _eventsAttached = true;
        }

        private void SettingsView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_eventsAttached)
            {
                return;
            }

            _settings.PropertyChanged -= Settings_PropertyChanged;
            _eventsAttached = false;
        }

        private void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_suspendDirtyTracking)
            {
                return;
            }

            UnsavedIndicator.Visibility = Visibility.Visible;
        }

        private async void SyncAll_Click(object sender, RoutedEventArgs e)
        {
            _service.ApplyConnectionSettings(_settings);
            _settings.SaveSettings();

            var success = await _service.SyncConfigAsync();
            if (success)
            {
                UnsavedIndicator.Visibility = Visibility.Collapsed;
                MessageBox.Show("Settings synchronized.", "Sync", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Failed to synchronize settings.", "Sync", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void PushConfigBtn_Click(object sender, RoutedEventArgs e)
        {
            _service.ApplyConnectionSettings(_settings);
            _settings.SaveSettings();

            var success = await _service.SyncBrainConfigAsync();
            if (success)
            {
                UnsavedIndicator.Visibility = Visibility.Collapsed;
            }
        }

        private void SaveProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = "medicai-profile.json",
                AddExtension = true
            };

            if (dialog.ShowDialog() == true)
            {
                _settings.SaveToPath(dialog.FileName);
            }
        }

        private void LoadProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var loaded = SavedSettings.LoadFromPath(dialog.FileName);

            _suspendDirtyTracking = true;
            _settings.ApplyFrom(loaded);
            _service.ApplyConnectionSettings(_settings);
            _suspendDirtyTracking = false;

            DataContext = null;
            DataContext = _settings;
            UnsavedIndicator.Visibility = Visibility.Visible;
        }

        private void DetectIpBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var hostEntry = Dns.GetHostEntry(Dns.GetHostName());
                var address = hostEntry.AddressList.FirstOrDefault(ip =>
                    ip.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(ip));

                if (address != null)
                {
                    BotIpInput.Text = address.ToString();
                    _settings.BotIp = address.ToString();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to detect IP: {ex.Message}", "IP Detection", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
