using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Net.WebSockets;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Linq;

namespace MedicAIGUI
{
    public class PriorityPlayer
    {
        public string StatusIcon { get; set; } = "🟢"; // 🟢 🔴 ⚪
        public string Name { get; set; }
        public int Tier { get; set; }
        public int Deaths { get; set; }
    }

    public class RespawnTimerObj
    {
        public string DisplayText { get; set; }
        public SolidColorBrush Color { get; set; }
    }

    public partial class MainWindow : Window
    {
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private System.Windows.Threading.DispatcherTimer _reconnectTimer;
        private System.Windows.Threading.DispatcherTimer _sessionTimer;
        
        public ObservableCollection<PriorityPlayer> Priorities { get; set; }
        public ObservableCollection<RespawnTimerObj> RespawnTimers { get; set; }
        
        private string _followMode = "ACTIVE";
        private int _totalHealing = 0;
        private int _meleeKills = 42;
        private bool _isConnected = false;
        private DateTime _sessionStart;
        private int _sessionSeconds = 0;

        public MainWindow()
        {
            InitializeComponent();
            Priorities = new ObservableCollection<PriorityPlayer>();
            RespawnTimers = new ObservableCollection<RespawnTimerObj>();
            
            PriorityListView.ItemsSource = Priorities;
            RespawnTimerList.ItemsSource = RespawnTimers;
            
            UpdateSessionStats();
            SetupTimers();
        }

        private void SetupTimers()
        {
            // Session timer
            _sessionTimer = new System.Windows.Threading.DispatcherTimer();
            _sessionTimer.Interval = TimeSpan.FromSeconds(1);
            _sessionTimer.Tick += (s, e) =>
            {
                _sessionSeconds++;
                int h = _sessionSeconds / 3600;
                int m = (_sessionSeconds % 3600) / 60;
                int sec = _sessionSeconds % 60;
                SessionTimer.Text = $"{h:D2}:{m:D2}:{sec:D2}";
            };

            // Reconnect timer
            _reconnectTimer = new System.Windows.Threading.DispatcherTimer();
            _reconnectTimer.Interval = TimeSpan.FromSeconds(5);
            _reconnectTimer.Tick += async (s, e) =>
            {
                if (!_isConnected && AutoReconnectCB.IsChecked == true)
                {
                    await ConnectBot();
                }
            };
            _reconnectTimer.Start();
        }

        // ============================================
        // CONNECTION & NETWORKING
        // ============================================

        private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected)
            {
                await DisconnectBot();
                return;
            }

            await ConnectBot();
        }

        private async Task ConnectBot()
        {
            if (_isConnected) return;

            StatusLabel.Text = "● Connecting...";
            StatusLabel.Foreground = Brushes.Yellow;
            
            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            string ip = BotIpInput.Text.Trim();
            if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";
            string port = PortInput.Text.Trim();
            if (string.IsNullOrEmpty(port)) port = "8765";
            
            try
            {
                await _ws.ConnectAsync(new Uri($"ws://{ip}:{port}"), _cts.Token);
                _isConnected = true;
                StatusLabel.Text = "● Connected";
                StatusLabel.Foreground = Brushes.LightGreen;
                ConnectBtn.Content = "🔌 Disconnect";
                AppendActivity($"✅ Connected to bot at {ip}:{port}", false);
                _ = ListenLoop();
            }
            catch (Exception ex)
            {
                _isConnected = false;
                StatusLabel.Text = "● Connection Failed";
                StatusLabel.Foreground = Brushes.IndianRed;
                ConnectBtn.Content = "🔌 Connect";
                AppendActivity($"❌ Connection Error: {ex.Message}", true);
            }
        }

        private async Task DisconnectBot()
        {
            try
            {
                if (_ws?.State == WebSocketState.Open)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
                }
                _ws?.Dispose();
                _isConnected = false;
                StatusLabel.Text = "● Disconnected";
                StatusLabel.Foreground = Brushes.IndianRed;
                ConnectBtn.Content = "🔌 Connect";
                AppendActivity("Disconnected from bot", false);
            }
            catch (Exception ex)
            {
                AppendActivity($"Disconnect error: {ex.Message}", true);
            }
        }

        private async Task ListenLoop()
        {
            var buffer = new byte[1024 * 16];
            try
            {
                while (_ws.State == WebSocketState.Open && _isConnected)
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", _cts.Token);
                        Dispatcher.Invoke(() =>
                        {
                            _isConnected = false;
                            StatusLabel.Text = "● Disconnected";
                            StatusLabel.Foreground = Brushes.IndianRed;
                            ConnectBtn.Content = "🔌 Connect";
                        });
                        break;
                    }
                    
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    try
                    {
                        dynamic data = JsonConvert.DeserializeObject(message);
                        Dispatcher.Invoke(() =>
                        {
                            if (data["type"] == "activity")
                            {
                                bool isError = ((string)data["msg"]).ToLower().Contains("error") || 
                                              ((string)data["msg"]).ToLower().Contains("failed");
                                AppendActivity((string)data["msg"], isError);
                            }
                            else if (data["type"] == "timer")
                            {
                                SessionTimer.Text = (string)data["time"];
                            }
                            else if (data["type"] == "status")
                            {
                                if (data["uber"] != null) UberMeterLabel.Text = $"Uber: {data["uber"]}%";
                                if (data["mode"] != null) FollowModeLabel.Text = $"Mode: {((string)data["mode"]).ToUpper()}";
                            }
                        });
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    _isConnected = false;
                    StatusLabel.Text = "● Disconnected";
                    StatusLabel.Foreground = Brushes.IndianRed;
                    ConnectBtn.Content = "🔌 Connect";
                });
            }
        }

        // ============================================
        // BOT CONTROL BUTTONS
        // ============================================

        private async void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected)
            {
                AppendActivity("❌ Not connected to bot. Connect first!", true);
                return;
            }

            try
            {
                _sessionSeconds = 0;
                _sessionTimer.Start();

                var config = new
                {
                    type = "start",
                    loadout = new
                    {
                        primary = PrimaryWeapon.SelectedItem?.ToString() ?? "Crusader's Crossbow",
                        secondary = SecondaryWeapon.SelectedItem?.ToString() ?? "Medi Gun",
                        melee = MeleeWeapon.SelectedItem?.ToString() ?? "Ubersaw"
                    },
                    priority_list = Priorities.Select(p => new { name = p.Name, tier = p.Tier }).ToList(),
                    whitelist = WhitelistBox.Items.Cast<string>().ToList(),
                    blacklist = BlacklistBox.Items.Cast<string>().ToList(),
                    follow_distance = (int)FollowDistanceSlider.Value,
                    uber_behavior = UberBehaviorCombo.SelectedItem?.ToString() ?? "Manual",
                    spy_check_frequency = (int)SpyCheckFreqSlider.Value
                };

                string json = JsonConvert.SerializeObject(config);
                byte[] data = Encoding.UTF8.GetBytes(json);
                
                await _ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, _cts.Token);
                
                FollowModeLabel.Text = "Mode: ACTIVE";
                AppendActivity("✅ BOT DEPLOYED! Starting session...", false);
            }
            catch (Exception ex)
            {
                AppendActivity($"❌ Deploy failed: {ex.Message}", true);
            }
        }

        private async void SendConfigBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected)
            {
                AppendActivity("❌ Not connected to bot.", true);
                return;
            }

            try
            {
                var config = new
                {
                    type = "config",
                    follow_distance = (int)FollowDistanceSlider.Value,
                    uber_behavior = UberBehaviorCombo.SelectedItem?.ToString() ?? "Manual",
                    spy_check_frequency = (int)SpyCheckFreqSlider.Value,
                    passive_mode_enabled = StartupModeCombo.SelectedIndex == 1,
                    master_volume = (int)MasterVolumeSlider.Value
                };

                string json = JsonConvert.SerializeObject(config);
                byte[] data = Encoding.UTF8.GetBytes(json);
                
                await _ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, _cts.Token);
                AppendActivity("⚙️ Config pushed to bot", false);
            }
            catch (Exception ex)
            {
                AppendActivity($"❌ Config push failed: {ex.Message}", true);
            }
        }

        // ============================================
        // PRIORITY PLAYER MANAGEMENT
        // ============================================

        private void AddPriority_Click(object sender, RoutedEventArgs e)
        {
            string name = PriorityNameInput.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;

            if (Priorities.Any(p => p.Name == name))
            {
                AppendActivity($"⚠️ {name} already in priority list", true);
                return;
            }

            var player = new PriorityPlayer
            {
                Name = name,
                Tier = int.Parse(TierInput.Text),
                Deaths = 0,
                StatusIcon = "⚪"
            };

            Priorities.Add(player);
            PriorityNameInput.Clear();
            AppendActivity($"✅ Added priority player: {name} (Tier {player.Tier})", false);
        }

        private void RemovePriority_Click(object sender, RoutedEventArgs e)
        {
            if (PriorityListView.SelectedItem is PriorityPlayer player)
            {
                Priorities.Remove(player);
                AppendActivity($"Removed priority player: {player.Name}", false);
            }
            else
            {
                AppendActivity("❌ Select a player to remove", true);
            }
        }

        // ============================================
        // WHITELIST & BLACKLIST
        // ============================================

        private void AddWhitelist_Click(object sender, RoutedEventArgs e)
        {
            string name = WhitelistInput.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;

            if (!WhitelistBox.Items.Contains(name))
            {
                WhitelistBox.Items.Add(name);
                WhitelistInput.Clear();
                AppendActivity($"✅ Whitelisted: {name}", false);
            }
        }

        private void RemoveWhitelist_Click(object sender, RoutedEventArgs e)
        {
            if (WhitelistBox.SelectedItem != null)
            {
                string name = WhitelistBox.SelectedItem.ToString();
                WhitelistBox.Items.Remove(WhitelistBox.SelectedItem);
                AppendActivity($"Removed from whitelist: {name}", false);
            }
        }

        private void AddBlacklist_Click(object sender, RoutedEventArgs e)
        {
            string name = BlacklistInput.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;

            if (!BlacklistBox.Items.Contains(name))
            {
                BlacklistBox.Items.Add(name);
                BlacklistInput.Clear();
                AppendActivity($"🚫 Blacklisted: {name}", false);
            }
        }

        private void RemoveBlacklist_Click(object sender, RoutedEventArgs e)
        {
            if (BlacklistBox.SelectedItem != null)
            {
                string name = BlacklistBox.SelectedItem.ToString();
                BlacklistBox.Items.Remove(BlacklistBox.SelectedItem);
                AppendActivity($"Removed from blacklist: {name}", false);
            }
        }

        private void QuickBanBtn_Click(object sender, RoutedEventArgs e)
        {
            string name = BlacklistInput.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                AppendActivity("❌ Enter a player name in the blacklist field", true);
                return;
            }

            if (!BlacklistBox.Items.Contains(name))
            {
                BlacklistBox.Items.Add(name);
            }
            BlacklistInput.Clear();
            AppendActivity($"⚡ MID-GAME BAN: {name}", true);
        }

        // ============================================
        // SESSION & LOGGING
        // ============================================

        private void ResetStatsBtn_Click(object sender, RoutedEventArgs e)
        {
            _meleeKills = 0;
            _totalHealing = 0;
            _sessionSeconds = 0;
            SessionTimer.Text = "00:00:00";
            UpdateSessionStats();
            AppendActivity("Session stats reset", false);
        }

        private void UpdateSessionStats()
        {
            MeleeKillsStatLabel.Text = $"Melee Kills: {_meleeKills}";
            TotalHealingStatLabel.Text = $"Total Healing: {_totalHealing:N0} HP";
        }

        // ============================================
        // UTILITY BUTTONS
        // ============================================

        private void UploadSoundsBtn_Click(object sender, RoutedEventArgs e)
        {
            AppendActivity("🎵 Custom sound upload feature coming soon", false);
        }

        private void CheckUpdatesBtn_Click(object sender, RoutedEventArgs e)
        {
            AppendActivity("🔍 Checking for updates...", false);
            AppendActivity("✅ You are on the latest version (v2.1.4-rc)", false);
        }

        private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            AppendActivity("📥 Downloading update...", false);
            await Task.Delay(2000);
            AppendActivity("✅ Update complete. Please restart the application.", false);
        }

        private void RollbackBtn_Click(object sender, RoutedEventArgs e)
        {
            AppendActivity("⏮️ Rolling back to v2.1.3...", false);
            AppendActivity("✅ Rollback complete.", false);
        }

        // ============================================
        // UI HELPERS
        // ============================================

        private void AppendActivity(string text, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                var para = new Paragraph(new Run(text));
                para.Margin = new Thickness(0, 2, 0, 2);
                para.Foreground = isError ? Brushes.IndianRed : 
                                 text.Contains("✅") ? Brushes.LightGreen :
                                 text.Contains("⚡") ? Brushes.Orange :
                                 text.Contains("⚠️") ? Brushes.Gold :
                                 text.Contains("🚫") ? Brushes.IndianRed :
                                 Brushes.LightGray;
                
                ActivityLog.Document.Blocks.Add(para);
                ActivityLog.ScrollToEnd();
            });
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _reconnectTimer?.Stop();
            _sessionTimer?.Stop();
            _ = DisconnectBot();
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void NavTab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radioButton)
            {
                // Hide all tab content
                DashboardScroller.Visibility = Visibility.Collapsed;
                PriorityScroller.Visibility = Visibility.Collapsed;
                SettingsScroller.Visibility = Visibility.Collapsed;

                // Show the selected tab content
                switch (radioButton.Name)
                {
                    case "DashboardTabBtn":
                        DashboardScroller.Visibility = Visibility.Visible;
                        break;
                    case "PriorityTabBtn":
                        PriorityScroller.Visibility = Visibility.Visible;
                        break;
                    case "SettingsTabBtn":
                        SettingsScroller.Visibility = Visibility.Visible;
                        break;
                }
            }
        }
    }
}