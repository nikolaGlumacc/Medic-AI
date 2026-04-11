using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MedicAIGUI.Services;

namespace MedicAIGUI.Views
{
    public partial class DashboardView : UserControl
    {
        private readonly MedicBotService _service = MedicBotService.Instance;
        private readonly DateTime _startTime = DateTime.Now;
        private bool _isInitialized = false;

        public DashboardView()
        {
            InitializeComponent();
            _isInitialized = true;

            _service.StatusUpdated     += OnStatusUpdated;
            _service.LogReceived       += OnLogReceived;
            _service.ConnectionChanged += OnConnectionChanged;

            OnConnectionChanged(_service.IsConnected);
        }

        private void OnConnectionChanged(bool connected)
        {
            if (!_isInitialized) return;
            Dispatcher.Invoke(() => {
                SetAllActionButtons(connected);
            });
        }

        private void SetAllActionButtons(bool enabled)
        {
            UberNowBtn.IsEnabled   = enabled;
            SpyCheckBtn.IsEnabled  = enabled;
            PushNowBtn.IsEnabled   = enabled;
            FallBackBtn.IsEnabled  = enabled;
            CallMedicBtn.IsEnabled = enabled;
            VoiceTrigBtn.IsEnabled = enabled;
            ReconnectBtn.IsEnabled = true;
        }

        private void OnStatusUpdated(TelemetrySnapshot data)
        {
            if (!_isInitialized) return;
            Dispatcher.Invoke(() =>
            {
                HealingStat.Text = $"{data.HealingOutput} HP/s";
                MeleeStat.Text   = data.MeleeKills.ToString();
                UptimeStat.Text  = DateTime.Now.Subtract(_startTime).ToString(@"hh\:mm\:ss");
                PingStat.Text    = $"{data.Latency:0}ms";

                bool running = data.IsRunning;
                SetAllActionButtons(running && _service.IsConnected);

                UpdateGraphs(data.TargetHPGraph);
            });
        }

        private void UpdateGraphs(List<double> hpPoints)
        {
            if (hpPoints == null || hpPoints.Count < 2) return;
            Sparkline.Points.Clear();
            double w = SparklineCanvas.ActualWidth;
            double h = SparklineCanvas.ActualHeight;
            if (w == 0) w = 600; // Fallback
            if (h == 0) h = 140;

            double xStep = w / Math.Max(1, hpPoints.Count - 1);
            for (int i = 0; i < hpPoints.Count; i++)
            {
                // Normalize HP (0-149) to height
                double y = h - (hpPoints[i] / 149.0 * h);
                Sparkline.Points.Add(new Point(i * xStep, y));
            }
        }

        private void OnLogReceived(string type, string msg)
        {
            if (!_isInitialized) return;
            Dispatcher.Invoke(() =>
            {
                Color color = type switch {
                    "OK"    => (Color)ColorConverter.ConvertFromString("#39D98A"),
                    "Error" => (Color)ColorConverter.ConvertFromString("#F2586B"),
                    "Warn"  => (Color)ColorConverter.ConvertFromString("#FFB830"),
                    "Info"  => (Color)ColorConverter.ConvertFromString("#4ED9FF"),
                    _       => (Color)ColorConverter.ConvertFromString("#8190A4")
                };

                var run = new Run($"[{DateTime.Now:HH:mm:ss}] {msg}") { Foreground = new SolidColorBrush(color) };
                var para = new Paragraph(run) { Margin = new Thickness(0, 0, 0, 4) };
                
                LogDoc.Blocks.Add(para);
                if (LogDoc.Blocks.Count > 50) LogDoc.Blocks.Remove(LogDoc.Blocks.FirstBlock);
                LogOutput.ScrollToEnd();
            });
        }

        private async void FireCmd(string endpoint, string label) => await _service.SendCommandAsync(endpoint, label);

        private void UberNow_Click(object sender, RoutedEventArgs e)    => FireCmd("uber_now",      "FORCE ÜBER");
        private void SpyCheck_Click(object sender, RoutedEventArgs e)   => FireCmd("spy_check",     "SPY COUNTER");
        private void PushNow_Click(object sender, RoutedEventArgs e)    => FireCmd("push",          "ENGAGE PUSH");
        private void FallBack_Click(object sender, RoutedEventArgs e)   => FireCmd("retreat",       "RETREAT");
        private void CallMedic_Click(object sender, RoutedEventArgs e)  => FireCmd("call_medic",    "CALL MEDIC");
        private void VoiceTrig_Click(object sender, RoutedEventArgs e)  => FireCmd("voice_trigger", "VOICE EVENT");
        private async void Reconnect_Click(object sender, RoutedEventArgs e) => await _service.SendCommandAsync("reconnect", "NODE LINK");
        
        private void Simulator_Click(object sender, RoutedEventArgs e)
        {
            _service.SimulationMode = !_service.SimulationMode;
            if (_service.SimulationMode)
            {
                OnLogReceived("Info", "SIMULATOR MODE ENABLED.");
            }
            else
            {
                OnLogReceived("Warn", "SIMULATOR MODE DISABLED.");
            }
        }
    }
}
