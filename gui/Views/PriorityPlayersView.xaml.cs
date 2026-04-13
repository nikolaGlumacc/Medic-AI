using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MedicAIGUI.Services;

namespace MedicAIGUI.Views
{
    public partial class PriorityPlayersView : UserControl
    {
        private readonly MedicBotService _service = MedicBotService.Instance;
        private readonly ObservableCollection<PriorityPlayer> _priorityPlayers = new();
        private readonly ObservableCollection<string> _whitelist = new();
        private readonly ObservableCollection<string> _blacklist = new();

        public PriorityPlayersView()
        {
            InitializeComponent();
            PriorityList.ItemsSource  = _priorityPlayers;
            WhitelistBox.ItemsSource  = _whitelist;
            BlacklistBox.ItemsSource  = _blacklist;
            LoadFromSettings();
        }

        // ── Persistence ──────────────────────────────────────────────────────

        private void LoadFromSettings()
        {
            var raw = _service.Settings.PriorityList ?? string.Empty;
            foreach (var entry in raw.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Split(':');
                if (parts.Length < 1) continue;
                var name = parts[0].Trim();
                var tier = parts.Length >= 2 && int.TryParse(parts[1], out var t) ? t : 1;
                var list = parts.Length >= 3 ? parts[2] : "priority";

                if (list == "white")
                    _whitelist.Add(name);
                else if (list == "black")
                    _blacklist.Add(name);
                else
                    _priorityPlayers.Add(new PriorityPlayer { Name = name, Tier = tier });
            }
        }

        private void SaveToSettings()
        {
            var parts = _priorityPlayers.Select(p => $"{p.Name}:{p.Tier}:priority")
                .Concat(_whitelist.Select(n => $"{n}:1:white"))
                .Concat(_blacklist.Select(n => $"{n}:1:black"));
            _service.Settings.PriorityList = string.Join("|", parts);
            _service.Settings.SaveSettings();
        }

        // ── Priority list ────────────────────────────────────────────────────

        private void AddPriorityBtn_Click(object sender, RoutedEventArgs e)
            => AddPriority();

        private void NewPlayerNameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddPriority();
        }

        private void AddPriority()
        {
            var name = NewPlayerNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            if (_priorityPlayers.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return;

            var tier = TierSelector.SelectedIndex + 1;
            _priorityPlayers.Add(new PriorityPlayer { Name = name, Tier = tier, StatusIcon = "✚" });
            NewPlayerNameBox.Clear();
            SaveToSettings();
        }

        private void RemovePriorityBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string name)
            {
                var entry = _priorityPlayers.FirstOrDefault(p => p.Name == name);
                if (entry != null)
                {
                    _priorityPlayers.Remove(entry);
                    SaveToSettings();
                }
            }
        }

        private void PriorityList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        // ── Whitelist ────────────────────────────────────────────────────────

        private void AddWhitelistBtn_Click(object sender, RoutedEventArgs e) => AddWhitelist();
        private void WhitelistInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddWhitelist();
        }

        private void AddWhitelist()
        {
            var name = WhitelistInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            if (_whitelist.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
            _whitelist.Add(name);
            WhitelistInput.Clear();
            SaveToSettings();
        }

        private void WhitelistBox_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (WhitelistBox.SelectedItem is string name)
            {
                _whitelist.Remove(name);
                SaveToSettings();
            }
        }

        // ── Blacklist ────────────────────────────────────────────────────────

        private void AddBlacklistBtn_Click(object sender, RoutedEventArgs e) => AddBlacklist();
        private void BlacklistInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddBlacklist();
        }

        private void AddBlacklist()
        {
            var name = BlacklistInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            if (_blacklist.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
            _blacklist.Add(name);
            BlacklistInput.Clear();
            SaveToSettings();
        }

        private void BlacklistBox_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (BlacklistBox.SelectedItem is string name)
            {
                _blacklist.Remove(name);
                SaveToSettings();
            }
        }

        // ── Toolbar buttons ──────────────────────────────────────────────────

        private async void SyncBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveToSettings();
            await _service.SyncConfigAsync();
        }

        private void ClearAllBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Clear all priority players, whitelist, and blacklist?",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            _priorityPlayers.Clear();
            _whitelist.Clear();
            _blacklist.Clear();
            SaveToSettings();
        }
    }
}