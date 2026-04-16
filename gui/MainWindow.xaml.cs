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
            ApplyTheme();
            _settings.PropertyChanged += (s, e) => ApplyTheme();
            NavigateTo(new DashboardView());
        }

        private void ApplyTheme()
        {
            try
            {
                var accent = (Color)ColorConverter.ConvertFromString(_settings.AccentColor);
                Application.Current.Resources["AccentCool"] = accent;
                Application.Current.Resources["AccentCoolBrush"] = new SolidColorBrush(accent);
                MainContainer.Opacity = _settings.UiOpacity;
                FontFamily = new FontFamily(_settings.GlobalFontFamily);
            }
            catch { }
        }

        private void NavigateTo(object view)
        {
            if (ViewHost == null) return;
            ViewHost.Content = view;
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
            string? name = (sender as RadioButton)?.Name;
            switch (name)
            {
                case "DashboardBtn": NavigateTo(new DashboardView()); break;
                case "PriorityBtn":  NavigateTo(new PriorityPlayersView()); break;
                case "SettingsBtn":  NavigateTo(new SettingsView()); break;
                case "TuningBtn":    NavigateTo(new TuningView()); break;
                case "MatrixBtn":    NavigateTo(new MatrixView()); break;
                case "LoadoutBtn":   NavigateTo(new LoadoutView()); break;
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        { if (e.ChangedButton == MouseButton.Left) DragMove(); }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
        private void MinimizeBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) => _settings.SaveSettings();
    }
}