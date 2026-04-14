using System.Windows;
using System.Windows.Controls;
using MedicAIGUI.Services;
using Newtonsoft.Json.Linq;

namespace MedicAIGUI.Views
{
    public partial class MatrixView : UserControl
    {
        private readonly MedicBotService _service = MedicBotService.Instance;

        public MatrixView()
        {
            InitializeComponent();
            _service.StatusUpdated += OnStatusUpdated;
            _service.ConnectionChanged += OnConnectionChanged;
            LoadHeuristicValues();
        }

        private void OnStatusUpdated(JObject status) { }
        private void OnConnectionChanged(bool connected) { }

        private void LoadHeuristicValues()
        {
            SldPowerWeight.Value = _service.Settings.PowerClassWeight;
            PowerWeightLabel.Text = SldPowerWeight.Value.ToString("F1");
        }

        private void HeuristicSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender == SldPowerWeight) PowerWeightLabel.Text = SldPowerWeight.Value.ToString("F1");
            else if (sender == SldSupportWeight) SupportWeightLabel.Text = SldSupportWeight.Value.ToString("F1");
            else if (sender == SldDistPenalty) DistPenaltyLabel.Text = SldDistPenalty.Value.ToString("F2");
            else if (sender == SldTier1Boost) Tier1BoostLabel.Text = SldTier1Boost.Value.ToString("F0") + "%";
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await _service.SyncConfigAsync();
            HeuristicsStatus.Text = "Config refreshed from bot.";
        }

        private void ApplyHeuristics_Click(object sender, RoutedEventArgs e)
        {
            _service.Settings.PowerClassWeight = SldPowerWeight.Value;
            _service.Settings.SaveSettings();
            HeuristicsStatus.Text = "Heuristics applied locally. Save to bot via Settings.";
        }
    }
}