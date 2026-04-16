using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MedicAIGUI.Services;

namespace MedicAIGUI.Views
{
    public partial class LoadoutView : UserControl
    {
        private readonly MedicBotService _service = MedicBotService.Instance;
        private readonly SavedSettings _settings;
        private bool _loaded;

        // ── Weapon lists ─────────────────────────────────────────────────────
        private static readonly List<string> PrimaryWeapons = new()
        {
            "Crusader's Crossbow",
            "Overdose",
            "Blutsauger",
            "Syringe Gun"
        };

        private static readonly List<string> SecondaryWeapons = new()
        {
            "Medi Gun",
            "Kritzkrieg",
            "Quick-Fix",
            "Vaccinator"
        };

        private static readonly List<string> MeleeWeapons = new()
        {
            "Ubersaw",
            "Amputator",
            "Solemn Vow",
            "Vita-Saw",
            "Bonesaw"
        };

        public LoadoutView()
        {
            InitializeComponent();
            _settings = _service.Settings;
            Loaded += LoadoutView_Loaded;
        }

        // ── Initialization ───────────────────────────────────────────────────
        private void LoadoutView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_loaded) return;
            _loaded = true;

            // Populate weapon lists
            PrimaryWeaponList.ItemsSource   = PrimaryWeapons;
            SecondaryWeaponList.ItemsSource = SecondaryWeapons;
            MeleeWeaponList.ItemsSource     = MeleeWeapons;

            // Restore saved selections; fall back to index 0 if not found
            PrimaryWeaponList.SelectedItem = _settings.PrimaryWeapon;
            if (PrimaryWeaponList.SelectedIndex < 0) PrimaryWeaponList.SelectedIndex = 0;

            SecondaryWeaponList.SelectedItem = _settings.SecondaryWeapon;
            if (SecondaryWeaponList.SelectedIndex < 0) SecondaryWeaponList.SelectedIndex = 0;

            MeleeWeaponList.SelectedItem = _settings.MeleeWeapon;
            if (MeleeWeaponList.SelectedIndex < 0) MeleeWeaponList.SelectedIndex = 0;

            UpdateStatusBar();
        }

        // ── Selection changed handlers ────────────────────────────────────────
        private void PrimaryWeaponList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            if (PrimaryWeaponList.SelectedItem is string weapon)
            {
                _settings.PrimaryWeapon = weapon;
                UpdateStatusBar();
            }
        }

        private void SecondaryWeaponList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            if (SecondaryWeaponList.SelectedItem is string weapon)
            {
                _settings.SecondaryWeapon = weapon;
                UpdateStatusBar();
            }
        }

        private void MeleeWeaponList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            if (MeleeWeaponList.SelectedItem is string weapon)
            {
                _settings.MeleeWeapon = weapon;
                UpdateStatusBar();
            }
        }

        // ── Button handlers ──────────────────────────────────────────────────
        private void SaveLoadoutBtn_Click(object sender, RoutedEventArgs e)
        {
            _settings.SaveSettings();
            SetStatus("✓ Loadout saved.");
        }

        private async void PushToBotBtn_Click(object sender, RoutedEventArgs e)
        {
            _settings.SaveSettings();
            await _service.SendConfigUpdate(new System.Collections.Generic.Dictionary<string, object>
            {
                ["primary_weapon"]   = _settings.PrimaryWeapon,
                ["secondary_weapon"] = _settings.SecondaryWeapon,
                ["melee_weapon"]     = _settings.MeleeWeapon
            });
            SetStatus("✓ Loadout pushed to bot.");
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private void UpdateStatusBar()
        {
            if (ActivePrimaryLabel != null)
                ActivePrimaryLabel.Text = _settings.PrimaryWeapon;
            if (ActiveSecondaryLabel != null)
                ActiveSecondaryLabel.Text = _settings.SecondaryWeapon;
            if (ActiveMeleeLabel != null)
                ActiveMeleeLabel.Text = _settings.MeleeWeapon;
        }

        private void SetStatus(string message)
        {
            if (StatusLabel != null)
                StatusLabel.Text = message;
        }
    }
}