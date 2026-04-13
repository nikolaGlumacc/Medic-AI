using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MedicAIGUI.Services;

namespace MedicAIGUI.Views
{
    // Real data model for a player row in the matrix
    public class MatrixPlayerRow
    {
        public string Name       { get; set; } = "";
        public string TierLabel  { get; set; } = "T1";
        public string TierBackground  { get; set; } = "#1C2840";
        public string TierForeground  { get; set; } = "#4ED9FF";
        public double BarWidth   { get; set; } = 100.0;
        public Color  BarColor   { get; set; } = (Color)ColorConverter.ConvertFromString("#39D98A");
        public string WeightLabel { get; set; } = "100%";
    }

    public partial class MatrixView : UserControl
    {
        private readonly MedicBotService _service = MedicBotService.Instance;
        private bool _loaded;

        public MatrixView()
        {
            InitializeComponent();
            Loaded   += MatrixView_Loaded;
            Unloaded += MatrixView_Unloaded;
        }

        private void MatrixView_Loaded(object sender, RoutedEventArgs e)
        {
            // Load heuristic slider values from settings
            SldPowerWeight.Value   = _service.Settings.PowerClassWeight;
            SldSupportWeight.Value = 1.2;   // sensible default (no separate property)
            SldDistPenalty.Value   = 0.4;   // sensible default
            SldTier1Boost.Value    = 30.0;  // 30% boost for Tier 1

            UpdateSliderLabels();
            RefreshPlayerList();

            if (!_loaded)
            {
                _service.StatusUpdated    += OnStatusUpdated;
                _service.ConnectionChanged += OnConnectionChanged;
                _loaded = true;
            }
        }

        private void MatrixView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            _service.StatusUpdated    -= OnStatusUpdated;
            _service.ConnectionChanged -= OnConnectionChanged;
            _loaded = false;
        }

        private void OnStatusUpdated(TelemetrySnapshot _) => Dispatcher.Invoke(RefreshPlayerList);
        private void OnConnectionChanged(bool _)          => Dispatcher.Invoke(RefreshPlayerList);

        // ── Core: parse settings and rebuild the player rows ─────────────────

        private void RefreshPlayerList()
        {
            var rows = BuildRows();
            PlayerList.ItemsSource = rows;
            EmptyLabel.Visibility  = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private List<MatrixPlayerRow> BuildRows()
        {
            var rows = new List<MatrixPlayerRow>();
            var raw  = _service.Settings.PriorityList ?? string.Empty;

            // Max bar width in the template (matches the Grid column width ~200px minus margins)
            const double maxBarPx = 180.0;

            double tier1Boost = SldTier1Boost?.Value ?? 30.0;

            foreach (var entry in raw.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Split(':');
                if (parts.Length < 1) continue;

                var name    = parts[0].Trim();
                var tier    = parts.Length >= 2 && int.TryParse(parts[1], out var t) ? t : 1;
                var listType = parts.Length >= 3 ? parts[2] : "priority";

                // Only show priority players in the matrix (not whitelist/blacklist)
                if (listType != "priority") continue;
                if (string.IsNullOrWhiteSpace(name)) continue;

                // Calculate heal weight: Tier 1 gets highest weight
                double baseWeight = tier switch
                {
                    1 => 100.0 * (1.0 + tier1Boost / 100.0),
                    2 => 70.0,
                    3 => 40.0,
                    _ => 20.0
                };
                // Clamp to 100 for display
                double displayWeight = Math.Min(baseWeight, 100.0);
                double barWidth      = maxBarPx * (displayWeight / 100.0);

                // Color based on tier
                var (barHex, bgHex, fgHex, tierLabel) = tier switch
                {
                    1 => ("#F2586B", "#2A1020", "#F2586B", "TIER 1"),
                    2 => ("#FFB830", "#2A1E08", "#FFB830", "TIER 2"),
                    3 => ("#39D98A", "#0A2018", "#39D98A", "TIER 3"),
                    _ => ("#4ED9FF", "#0A1828", "#4ED9FF", $"TIER {tier}"),
                };

                rows.Add(new MatrixPlayerRow
                {
                    Name           = name,
                    TierLabel      = tierLabel,
                    TierBackground = bgHex,
                    TierForeground = fgHex,
                    BarWidth       = barWidth,
                    BarColor       = (Color)ColorConverter.ConvertFromString(barHex),
                    WeightLabel    = $"{displayWeight:0}%",
                });
            }

            // Sort: Tier 1 first
            rows.Sort((a, b) => string.Compare(a.TierLabel, b.TierLabel, StringComparison.Ordinal));
            return rows;
        }

        // ── Heuristic sliders ─────────────────────────────────────────────────

        private void HeuristicSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            UpdateSliderLabels();
            RefreshPlayerList(); // Re-calculate weights with new boost value
        }

        private void UpdateSliderLabels()
        {
            if (PowerWeightLabel   != null) PowerWeightLabel.Text   = $"{SldPowerWeight.Value:F1}x";
            if (SupportWeightLabel != null) SupportWeightLabel.Text = $"{SldSupportWeight.Value:F1}x";
            if (DistPenaltyLabel   != null) DistPenaltyLabel.Text   = $"{SldDistPenalty.Value:F2}";
            if (Tier1BoostLabel    != null) Tier1BoostLabel.Text    = $"{SldTier1Boost.Value:0}%";
        }

        private async void ApplyHeuristics_Click(object sender, RoutedEventArgs e)
        {
            // Save power class weight to settings (only one that has a property)
            _service.Settings.PowerClassWeight = SldPowerWeight.Value;
            _service.Settings.SaveSettings();

            // Build a config patch with all heuristic values and sync to bot
            var patch = new
            {
                power_class_weight   = SldPowerWeight.Value,
                support_class_weight = SldSupportWeight.Value,
                distance_penalty     = SldDistPenalty.Value,
                tier1_heal_boost_pct = SldTier1Boost.Value,
            };

            var ok = await _service.SyncConfigAsync(patch);

            HeuristicsStatus.Text       = ok ? "✓ Heuristics applied" : "✗ Sync failed";
            HeuristicsStatus.Foreground = ok
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#39D98A"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F2586B"));

            // Auto-clear status after 3 seconds
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, __) => { HeuristicsStatus.Text = ""; timer.Stop(); };
            timer.Start();

            RefreshPlayerList();
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshPlayerList();
        }
    }
}