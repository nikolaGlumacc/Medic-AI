using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MedicAIGUI.Services;
using Newtonsoft.Json.Linq;

namespace MedicAIGUI.Views
{
    public partial class DashboardView : UserControl
    {
        private readonly MedicBotService _service = MedicBotService.Instance;
        private ObservableCollection<string> _weapons = new();
        private bool _autoScroll = true;

        public DashboardView()
        {
            InitializeComponent();
            WeaponsList.ItemsSource = _weapons;
            _service.StatusUpdated += OnStatusUpdated;
            _service.LogReceived += OnLogReceived;
            _service.ConnectionChanged += OnConnectionChanged;

            UpdateButtonState(_service.IsConnected);
        }

        private void OnStatusUpdated(JObject status)
        {
            Dispatcher.Invoke(() =>
            {
                HealingStat.Text = $"{status["healing"]?.Value<int>() ?? 0} HP/s";
                MeleeStat.Text = status["melee_kills"]?.ToString() ?? "0";
                var secs = status["session_seconds"]?.Value<int>() ?? 0;
                UptimeStat.Text = $"{secs / 3600:D2}:{(secs % 3600) / 60:D2}:{secs % 60:D2}";
                PingStat.Text = $"{status["latency"]?.Value<int>() ?? 0} ms";
                UpdateSparkline(status["uber"]?.Value<double>() ?? 0);
            });
        }

        private void UpdateSparkline(double uber)
        {
            // Simple sparkline: shift points left, add new point
            var points = Sparkline.Points;
            if (points.Count > 50) points.RemoveAt(0);
            points.Add(new System.Windows.Point(points.Count * 8, 50 - uber / 2));
        }

        private void OnLogReceived(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                LogOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
                if (_autoScroll) LogOutput.ScrollToEnd();
            });
        }

        private void OnConnectionChanged(bool connected)
        {
            Dispatcher.Invoke(() => UpdateButtonState(connected));
        }

        private void UpdateButtonState(bool connected)
        {
            StartBotBtn.IsEnabled = connected;
            StopBotBtn.IsEnabled = connected;
            RefreshBtn.IsEnabled = connected;
            SyncConfigBtn.IsEnabled = connected;
            FetchWeaponsBtn.IsEnabled = connected;
            DebugSnapshotBtn.IsEnabled = connected;

            if (ConnIndicator != null)
            {
                ConnIndicator.Fill = new SolidColorBrush(connected ? (Color)ColorConverter.ConvertFromString("#39D98A") : (Color)ColorConverter.ConvertFromString("#F2586B"));
                if (ConnIndicator.Effect is System.Windows.Media.Effects.DropShadowEffect ds)
                {
                    ds.Color = connected ? (Color)ColorConverter.ConvertFromString("#39D98A") : (Color)ColorConverter.ConvertFromString("#F2586B");
                }
            }
        }

        // Event handlers
        private async void StartBotBtn_Click(object sender, RoutedEventArgs e) => await _service.StartBotAsync();
        private async void StopBotBtn_Click(object sender, RoutedEventArgs e) => await _service.StopBotAsync();
        private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await _service.RefreshStatusAsync();
        private async void SyncConfigBtn_Click(object sender, RoutedEventArgs e) => await _service.SyncConfigAsync();
        private async void FetchWeaponsBtn_Click(object sender, RoutedEventArgs e)
        {
            var weapons = await _service.GetWeaponsAsync();
            Dispatcher.Invoke(() =>
            {
                _weapons.Clear();
                foreach (var w in weapons) _weapons.Add(w);
            });
        }
        private void SimulationBtn_Click(object sender, RoutedEventArgs e)
        {
            _service.ToggleSimulation();
            var isSim = _service.IsSimulationMode;
            SimulationBtn.Opacity = isSim ? 1.0 : 0.6;
            SimulationBtn.Content = isSim ? "STOP SIMULATION" : "SIMULATION MODE";
            SimulationBtn.Background = isSim ? (SolidColorBrush)FindResource("AccentCoolBrush") : (SolidColorBrush)FindResource("SurfaceLightBrush");
        }
        private void DebugSnapshotBtn_Click(object sender, RoutedEventArgs e) => _service.DebugSnapshotAsync();

        private async void DiagnosticBtn_Click(object sender, RoutedEventArgs e)
        {
            LogOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] [DIAGNOSTIC] Running system health check...\n");
            LogOutput.ScrollToEnd();
            
            DiagnosticBtn.IsEnabled = false;
            DiagnosticBtn.Content = "TESTING...";
            
            try
            {
                string report = await _service.CheckHealthAsync();
                
                // Print to log
                LogOutput.AppendText($"\n{report}\n");
                LogOutput.ScrollToEnd();

                // If health check passed but we are not connected, try one last time
                if (report.Contains("✅") && !_service.IsConnected)
                {
                    LogOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] [DIAGNOSTIC] Health OK, retrying connection...\n");
                    await _service.ConnectAsync();
                }
            }
            catch (Exception ex)
            {
                LogOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] [ERROR] Diagnostic failed: {ex.Message}\n");
            }
            finally
            {
                DiagnosticBtn.IsEnabled = true;
                DiagnosticBtn.Content = "DIAGNOSTIC";
            }
        }

        // Log toolbar
        private void CopyLogBtn_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(new TextRange(LogOutput.Document.ContentStart, LogOutput.Document.ContentEnd).Text);
        }
        private void ScrollLockBtn_Click(object sender, RoutedEventArgs e)
        {
            _autoScroll = !_autoScroll;
            ScrollStateText.Text = _autoScroll ? "Auto-scroll" : "Scroll locked";
        }
        private void FilterAllBtn_Click(object sender, RoutedEventArgs e) { }
        private void FilterOkBtn_Click(object sender, RoutedEventArgs e) { }
        private void FilterErrBtn_Click(object sender, RoutedEventArgs e) { }
        private void FilterSyncBtn_Click(object sender, RoutedEventArgs e) { }
        private void FilterWarnBtn_Click(object sender, RoutedEventArgs e) { }
        private void ActivitySearchBox_TextChanged(object sender, TextChangedEventArgs e) { }
    }
}