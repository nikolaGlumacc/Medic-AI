using System;
using System.Collections.Generic;
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
        private bool _suspendDirtyTracking;

        public SettingsView()
        {
            InitializeComponent();
            _settings = _service.Settings;
            DataContext = _settings;
            Loaded += (s, e) => _settings.PropertyChanged += OnSettingChanged;
            Unloaded += (s, e) => _settings.PropertyChanged -= OnSettingChanged;
        }

        private void OnSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!_suspendDirtyTracking) UnsavedIndicator.Visibility = Visibility.Visible;
        }

        private async void SaveToBot_Click(object sender, RoutedEventArgs e)
        {
            var config = new Dictionary<string, object>
            {
                ["mouse_speed"] = _settings.MouseSpeed,
                ["deadzone_px"] = _settings.DeadzonePx,
                ["max_move_px"] = _settings.MaxMovePx,
                ["max_dist"] = _settings.MaxDist,
                ["aim_lead_factor"] = _settings.AimLeadFactor,
                ["pid_kp"] = _settings.PidKp,
                ["pid_ki"] = _settings.PidKi,
                ["pid_kd"] = _settings.PidKd,
                ["follow_fwd_thresh"] = _settings.FollowFwdThresh,
                ["follow_back_thresh"] = _settings.FollowBackThresh,
                ["follow_strafe_thresh"] = _settings.FollowStrafeThresh,
                ["track_min_hits"] = _settings.TrackMinHits,
                ["track_max_lost"] = _settings.TrackMaxLost,
                ["lock_duration"] = _settings.LockDuration,
                ["blu_h_min"] = _settings.BluHMin, ["blu_h_max"] = _settings.BluHMax,
                ["blu_s_min"] = _settings.BluSMin, ["blu_s_max"] = _settings.BluSMax,
                ["blu_v_min"] = _settings.BluVMin, ["blu_v_max"] = _settings.BluVMax,
                ["red1_h_min"] = _settings.Red1HMin, ["red1_h_max"] = _settings.Red1HMax,
                ["red2_h_min"] = _settings.Red2HMin, ["red2_h_max"] = _settings.Red2HMax,
            };
            await _service.SendConfigUpdate(config);
            UnsavedIndicator.Visibility = Visibility.Collapsed;
        }

        private async void SyncAll_Click(object sender, RoutedEventArgs e)
        {
            // PRO FIX: We use SyncConfigAsync which now serializes the WHOLE object with proper JsonPropertyName mapping.
            // No more manual dictionary building needed here.
            await _service.SyncConfigAsync();
            UnsavedIndicator.Visibility = Visibility.Collapsed;
        }

        private void SaveProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = "profile.json" };
            if (dlg.ShowDialog() == true) _settings.SaveToPath(dlg.FileName);
        }

        private void LoadProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
            if (dlg.ShowDialog() == true)
            {
                _suspendDirtyTracking = true;
                _settings.ApplyFrom(SavedSettings.LoadFromPath(dlg.FileName));
                DataContext = null; DataContext = _settings;
                _suspendDirtyTracking = false;
                UnsavedIndicator.Visibility = Visibility.Visible;
            }
        }

        private void DetectIpBtn_Click(object sender, RoutedEventArgs e)
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ip != null) BotIpInput.Text = ip.ToString();
        }

        private void PushConfigBtn_Click(object sender, RoutedEventArgs e) => SaveToBot_Click(sender, e);
    }
}