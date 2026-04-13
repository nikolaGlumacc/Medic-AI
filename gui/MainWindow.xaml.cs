using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using MedicAIGUI.Services;
using MedicAIGUI.Views;

namespace MedicAIGUI
{
    public partial class MainWindow : Window
    {
        private readonly MedicBotService _service = MedicBotService.Instance;
        private SavedSettings _settings;

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
            ApplyUpdateSnapshot(false);
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
                case "TuningBtn":    NavigateTo(new TuningView()); break;
                case "MatrixBtn":    NavigateTo(new MatrixView()); break;
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
    }
}