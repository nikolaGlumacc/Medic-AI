using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MedicAIGUI.Services;
using MedicAIGUI.Views;

namespace MedicAIGUI
{
    public partial class MainWindow : Window
    {
        private readonly MedicBotService _service = MedicBotService.Instance;
        private SavedSettings _settings;
        private Dictionary<string, string> _weaponImages;

        public MainWindow()
        {
            InitializeComponent();
            _service.LoadLocalSettings();
            _settings = _service.Settings;
            _service.ApplyConnectionSettings(_settings);
            
            // Apply theme on startup
            ApplyTheme();
            
            // Listen for changes
            _settings.PropertyChanged += (s, e) => ApplyTheme();

            // Default view
            NavigateTo(new DashboardView());
            DebugLog("NAV_DASHBOARD");
            ApplyUpdateSnapshot(false);

            // Load weapon images
            LoadWeaponImages();
        }

        private void ApplyTheme()
        {
            try
            {
                // 1. Accent Color
                var accentColor = (Color)ColorConverter.ConvertFromString(_settings.AccentColor);
                Application.Current.Resources["AccentCool"] = accentColor;
                Application.Current.Resources["AccentCoolBrush"] = new SolidColorBrush(accentColor);

                // 2. Opacity
                MainContainer.Opacity = _settings.UiOpacity;

                // 3. Font
                var font = new FontFamily(_settings.GlobalFontFamily);
                this.FontFamily = font;
            }
            catch { }
        }

        private void NavigateTo(object view)
        {
            if (ViewHost == null) return;
            ViewHost.Content = view;
            
            // Premium Entrance Animation
            var sb = (Storyboard)Application.Current.TryFindResource("FadeInView");
            if (sb != null)
            {
                ViewHost.RenderTransform = new TranslateTransform(0, 20);
                sb.Begin(ViewHost);
            }
        }

        private void NavBtn_Checked(object sender, RoutedEventArgs e)
        {
            if (ViewHost == null) return;
            string? viewName = (sender as RadioButton)?.Name;
            switch (viewName)
            {
                case "DashboardBtn": NavigateTo(new DashboardView()); break;
                case "PriorityBtn":  NavigateTo(new PriorityPlayersView()); break;
                case "SettingsBtn":  NavigateTo(new SettingsView()); break;
            }
        }

        private void ApplyUpdateSnapshot(bool updatesAvailable)
        {
            if (UpdateNavBadge != null)
            {
                UpdateNavBadge.Visibility = updatesAvailable ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
        private void MinimizeBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) => _settings.SaveSettings();

        private void LoadWeaponImages()
        {
            var weaponsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "weapons");
            if (Directory.Exists(weaponsPath))
            {
                _weaponImages = new Dictionary<string, string>();
                foreach (var file in Directory.GetFiles(weaponsPath, "*.png"))
                {
                    var weaponName = Path.GetFileNameWithoutExtension(file);
                    _weaponImages[weaponName] = file;
                }
            }

            // Bind the weapon images to the grid
            PrimaryWeaponGrid.ItemsSource = _weaponImages.Keys;
            SecondaryWeaponGrid.ItemsSource = _weaponImages.Keys;
            MeleeWeaponGrid.ItemsSource = _weaponImages.Keys;

            PrimaryWeaponGrid.ItemContainerStyle = (Style)FindResource("WeaponItemTemplate");
            SecondaryWeaponGrid.ItemContainerStyle = (Style)FindResource("WeaponItemTemplate");
            MeleeWeaponGrid.ItemContainerStyle = (Style)FindResource("WeaponItemTemplate");

            PrimaryWeaponGrid.SelectionChanged += WeaponGrid_SelectionChanged;
            SecondaryWeaponGrid.SelectionChanged += WeaponGrid_SelectionChanged;
            MeleeWeaponGrid.SelectionChanged += WeaponGrid_SelectionChanged;
        }

        private void WeaponGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var grid = sender as ListBox;
            if (grid != null && grid.SelectedItem != null)
            {
                var weaponName = grid.SelectedItem.ToString();
                switch (grid.Name)
                {
                    case "PrimaryWeaponGrid":
                        _settings.PrimaryWeapon = weaponName;
                        break;
                    case "SecondaryWeaponGrid":
                        _settings.SecondaryWeapon = weaponName;
                        break;
                    case "MeleeWeaponGrid":
                        _settings.MeleeWeapon = weaponName;
                        break;
                }
            }
        }
    }
}
