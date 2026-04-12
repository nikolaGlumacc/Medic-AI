using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MedicAIGUI.Services;

namespace MedicAIGUI.Views
{
    public partial class DashboardView : UserControl
    {
        private sealed class ActivityEntry
        {
            public DateTime Timestamp { get; init; }
            public string Type { get; init; } = "Info";
            public string Message { get; init; } = string.Empty;
        }

        private readonly MedicBotService _service = MedicBotService.Instance;
        private readonly DateTime _startTime = DateTime.Now;
        private readonly List<ActivityEntry> _activityEntries = new List<ActivityEntry>();
        private readonly FlowDocument _logDocument = new FlowDocument();
        private bool _isInitialized;
        private bool _subscriptionsAttached;
        private bool _scrollLocked;
        private string _activeLogFilter = "ALL";
        private string _logSearchTerm = string.Empty;

        public DashboardView()
        {
            InitializeComponent();

            LogOutput.Document = _logDocument;
            Loaded += DashboardView_Loaded;
            Unloaded += DashboardView_Unloaded;
        }

        private void DashboardView_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_subscriptionsAttached)
            {
                _service.StatusUpdated += OnStatusUpdated;
                _service.LogReceived += OnLogReceived;
                _service.ConnectionChanged += OnConnectionChanged;
                _subscriptionsAttached = true;
            }

            _isInitialized = true;
            OnConnectionChanged(_service.IsConnected);
            OnStatusUpdated(_service.LastTelemetry);
        }

        private void DashboardView_Unloaded(object sender, RoutedEventArgs e)
        {
            _isInitialized = false;

            if (!_subscriptionsAttached)
            {
                return;
            }

            _service.StatusUpdated -= OnStatusUpdated;
            _service.LogReceived -= OnLogReceived;
            _service.ConnectionChanged -= OnConnectionChanged;
            _subscriptionsAttached = false;
        }

        private void OnConnectionChanged(bool connected)
        {
            if (!_isInitialized)
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                RefreshBtn.IsEnabled = true;
                SyncConfigBtn.IsEnabled = true;
                FetchWeaponsBtn.IsEnabled = true;
                DebugSnapshotBtn.IsEnabled = connected;
            });
        }

        private void OnStatusUpdated(TelemetrySnapshot data)
        {
            if (!_isInitialized)
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                HealingStat.Text = $"{data.HealingOutput} HP/s";
                MeleeStat.Text = data.MeleeKills.ToString();
                UptimeStat.Text = DateTime.Now.Subtract(_startTime).ToString(@"hh\:mm\:ss");
                PingStat.Text = $"{data.Latency:0} ms";
                UpdateGraphs(data.TargetHPGraph);
            });
        }

        private void OnLogReceived(string type, string msg)
        {
            if (!_isInitialized)
            {
                return;
            }

            Dispatcher.Invoke(() => AppendActivity(type, msg));
        }

        private void AppendActivity(string type, string msg)
        {
            _activityEntries.Add(new ActivityEntry
            {
                Timestamp = DateTime.Now,
                Type = string.IsNullOrWhiteSpace(type) ? "Info" : type,
                Message = msg ?? string.Empty
            });

            while (_activityEntries.Count > 500)
            {
                _activityEntries.RemoveAt(0);
            }

            RefreshActivityLog();
        }

        private void RefreshActivityLog()
        {
            _logDocument.Blocks.Clear();

            foreach (var entry in _activityEntries.Where(MatchesFilter))
            {
                var run = new Run($"[{entry.Timestamp:HH:mm:ss}] [{entry.Type.ToUpperInvariant()}] {entry.Message}")
                {
                    Foreground = new SolidColorBrush(GetColor(entry.Type))
                };

                _logDocument.Blocks.Add(new Paragraph(run) { Margin = new Thickness(0, 0, 0, 4) });
            }

            while (_logDocument.Blocks.Count > 500)
            {
                _logDocument.Blocks.Remove(_logDocument.Blocks.FirstBlock);
            }

            if (!_scrollLocked)
            {
                LogOutput.ScrollToEnd();
            }
        }

        private bool MatchesFilter(ActivityEntry entry)
        {
            var type = entry.Type.ToUpperInvariant();
            var message = entry.Message ?? string.Empty;

            var matchesFilter = _activeLogFilter switch
            {
                "ALL" => true,
                "OK" => type == "OK",
                "ERR" => type == "ERROR" || type == "ERR",
                "WARN" => type == "WARN" || type == "WARNING",
                "SYNC" => type == "SYNC" || message.IndexOf("sync", StringComparison.OrdinalIgnoreCase) >= 0,
                _ => true
            };

            if (!matchesFilter)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_logSearchTerm))
            {
                return true;
            }

            return message.IndexOf(_logSearchTerm, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   type.IndexOf(_logSearchTerm, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Color GetColor(string type)
        {
            return type.ToUpperInvariant() switch
            {
                "OK" => (Color)ColorConverter.ConvertFromString("#39D98A"),
                "ERROR" => (Color)ColorConverter.ConvertFromString("#F2586B"),
                "ERR" => (Color)ColorConverter.ConvertFromString("#F2586B"),
                "WARN" => (Color)ColorConverter.ConvertFromString("#FFB830"),
                "WARNING" => (Color)ColorConverter.ConvertFromString("#FFB830"),
                "SYNC" => (Color)ColorConverter.ConvertFromString("#8AB4F8"),
                _ => (Color)ColorConverter.ConvertFromString("#4ED9FF")
            };
        }

        private void UpdateGraphs(List<double> hpPoints)
        {
            Sparkline.Points.Clear();

            if (hpPoints == null || hpPoints.Count < 2)
            {
                return;
            }

            var width = SparklineCanvas.ActualWidth;
            var height = SparklineCanvas.ActualHeight;

            if (width <= 0)
            {
                width = 600;
            }

            if (height <= 0)
            {
                height = 140;
            }

            var xStep = width / Math.Max(1, hpPoints.Count - 1);
            for (var i = 0; i < hpPoints.Count; i++)
            {
                var y = height - (hpPoints[i] / 150.0 * height);
                Sparkline.Points.Add(new Point(i * xStep, y));
            }
        }

        private void SetFilter(string filter)
        {
            _activeLogFilter = filter;
            RefreshActivityLog();
        }

        private async Task RefreshWeaponsAsync()
        {
            var weapons = await _service.GetWeaponsAsync();
            WeaponsList.ItemsSource = weapons;
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await _service.RefreshStatusAsync();
        }

        private async void SyncConfigBtn_Click(object sender, RoutedEventArgs e)
        {
            await _service.SyncConfigAsync();
        }

        private async void FetchWeaponsBtn_Click(object sender, RoutedEventArgs e)
        {
            await RefreshWeaponsAsync();
        }

        private async void DebugSnapshotBtn_Click(object sender, RoutedEventArgs e)
        {
            await _service.DebugSnapshotAsync();
        }

        private void SimulationBtn_Click(object sender, RoutedEventArgs e)
        {
            _service.SimulationMode = !_service.SimulationMode;
            AppendActivity("Info", _service.SimulationMode ? "Simulation mode enabled." : "Simulation mode disabled.");
        }

        private void CopyLogBtn_Click(object sender, RoutedEventArgs e)
        {
            var text = new TextRange(LogOutput.Document.ContentStart, LogOutput.Document.ContentEnd).Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                Clipboard.SetText(text);
            }
        }

        private void ScrollLockBtn_Click(object sender, RoutedEventArgs e)
        {
            _scrollLocked = !_scrollLocked;
            ScrollLockBtn.Content = _scrollLocked ? "Unlock Scroll" : "Lock Scroll";
            ScrollStateText.Text = _scrollLocked ? "Scroll locked" : "Auto-scroll";
        }

        private void FilterAllBtn_Click(object sender, RoutedEventArgs e) => SetFilter("ALL");
        private void FilterOkBtn_Click(object sender, RoutedEventArgs e) => SetFilter("OK");
        private void FilterErrBtn_Click(object sender, RoutedEventArgs e) => SetFilter("ERR");
        private void FilterSyncBtn_Click(object sender, RoutedEventArgs e) => SetFilter("SYNC");
        private void FilterWarnBtn_Click(object sender, RoutedEventArgs e) => SetFilter("WARN");

        private void ActivitySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _logSearchTerm = ActivitySearchBox.Text?.Trim() ?? string.Empty;
            RefreshActivityLog();
        }
    }
}
