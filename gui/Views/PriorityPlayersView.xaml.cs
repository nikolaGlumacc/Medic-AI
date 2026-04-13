using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MedicAIGUI.Services;

namespace MedicAIGUI.Views
{
    public partial class PriorityPlayersView : UserControl
    {
        private readonly MedicBotService _service = MedicBotService.Instance;
        private ObservableCollection<PriorityPlayerItem> _priorityList = new();
        private ObservableCollection<string> _whitelist = new();
        private ObservableCollection<string> _blacklist = new();

        public class PriorityPlayerItem
        {
            public string Name { get; set; } = "";
            public int Tier { get; set; } = 1;
            public int Deaths { get; set; } = 0;
            public string StatusIcon => "●";
        }

        public PriorityPlayersView()
        {
            InitializeComponent();
            PriorityList.ItemsSource = _priorityList;
            WhitelistBox.ItemsSource = _whitelist;
            BlacklistBox.ItemsSource = _blacklist;
            Loaded += (s, e) => LoadLists();
        }

        private void LoadLists()
        {
            _priorityList.Clear();
            foreach (var name in _service.Settings.PriorityList)
                _priorityList.Add(new PriorityPlayerItem { Name = name, Tier = 1 });
        }

        private void AddPriorityBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(NewPlayerNameBox.Text))
            {
                _priorityList.Add(new PriorityPlayerItem
                {
                    Name = NewPlayerNameBox.Text.Trim(),
                    Tier = TierSelector.SelectedIndex + 1
                });
                NewPlayerNameBox.Clear();
            }
        }

        private void RemovePriorityBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string name)
            {
                var item = _priorityList.FirstOrDefault(p => p.Name == name);
                if (item != null) _priorityList.Remove(item);
            }
        }

        private void ClearAllBtn_Click(object sender, RoutedEventArgs e) => _priorityList.Clear();

        private async void SyncBtn_Click(object sender, RoutedEventArgs e)
        {
            _service.Settings.PriorityList = _priorityList.Select(p => p.Name).ToList();
            await _service.SendConfigUpdate(new() { ["priority_players"] = _priorityList.Select(p => p.Name).ToList() });
            _service.Settings.SaveSettings();
            MessageBox.Show("Priority list saved to bot.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void PriorityList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
        private void NewPlayerNameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddPriorityBtn_Click(sender, e);
        }
        private void AddWhitelistBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(WhitelistInput.Text))
            {
                _whitelist.Add(WhitelistInput.Text.Trim());
                WhitelistInput.Clear();
            }
        }
        private void WhitelistInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddWhitelistBtn_Click(sender, e);
        }
        private void WhitelistBox_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (WhitelistBox.SelectedItem is string item) _whitelist.Remove(item);
        }
        private void AddBlacklistBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(BlacklistInput.Text))
            {
                _blacklist.Add(BlacklistInput.Text.Trim());
                BlacklistInput.Clear();
            }
        }
        private void BlacklistInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) AddBlacklistBtn_Click(sender, e);
        }
        private void BlacklistBox_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (BlacklistBox.SelectedItem is string item) _blacklist.Remove(item);
        }
    }
}