using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using MedicAIGUI.Services;
using MedicAIGUI.Views;

namespace MedicAIGUI
{
    public partial class MainWindow : Window
    {
        private readonly MedicBotService _service = MedicBotService.Instance;
        private SavedSettings _settings;

        // 🔥 Inspector Pro v2 live event feed
        public ObservableCollection<string> DebugEvents => DebugHub.Events;

        public MainWindow()
        {
            InitializeComponent();

            DataContext = this;

            _service.LoadLocalSettings();
            _settings = _service.Settings;
            _service.ApplyConnectionSettings(_settings);

            ApplyTheme();
            _settings.PropertyChanged += (s, e) => ApplyTheme();

            NavigateTo(new DashboardView());

            // 🔥 Inspector startup log
            DebugHub.Log("APP STARTED");
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
            catch (Exception ex)
            {
                DebugHub.Log("THEME_ERROR: " + ex.Message);
            }
        }

        private void NavigateTo(object view)
        {
            if (ViewHost == null) return;

            ViewHost.Content = view;

            DebugHub.Log($"NAVIGATED: {view.GetType().Name}");

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
                case "DashboardBtn":  NavigateTo(new DashboardView());       break;
                case "PriorityBtn":   NavigateTo(new PriorityPlayersView()); break;
                case "SettingsBtn":   NavigateTo(new SettingsView());        break;
                case "TuningBtn":     NavigateTo(new TuningView());          break;
                case "MatrixBtn":     NavigateTo(new MatrixView());          break;
                case "LoadoutBtn":    NavigateTo(new LoadoutView());         break;
                case "InspectorBtn":  NavigateTo(new InspectorView());       break;
            }

            DebugHub.Log($"BUTTON_CLICK: {name}");
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
                DebugHub.Log("WINDOW_DRAG");
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            DebugHub.Log("APP_CLOSING");
            _settings.SaveSettings();
            DebugHub.Log("SETTINGS_SAVED");
            Close();
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
            DebugHub.Log("APP_MINIMIZED");
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _settings.SaveSettings();
            DebugHub.Log("SETTINGS_SAVED_ON_CLOSE");
        }
    }
}