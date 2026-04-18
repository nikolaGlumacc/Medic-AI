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
        private readonly System.Collections.Generic.Dictionary<string, UserControl> _viewCache = new();

        // 🔥 Inspector Pro v5 live event feed
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

            NavigateTo(GetView<DashboardView>());

            // 🔥 Global Bot Connection
            DebugHub.Log("MAIN: Initializing global connection...");
            Loaded += async (s, e) => await _service.ConnectAsync();

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
                case "DashboardBtn":  NavigateTo(GetView<DashboardView>());       break;
                case "PriorityBtn":   NavigateTo(GetView<PriorityPlayersView>()); break;
                case "SettingsBtn":   NavigateTo(GetView<SettingsView>());        break;
                case "TuningBtn":     NavigateTo(GetView<TuningView>());          break;
                case "MatrixBtn":     NavigateTo(GetView<MatrixView>());          break;
                case "LoadoutBtn":    NavigateTo(GetView<LoadoutView>());         break;
                case "InspectorBtn":  NavigateTo(GetView<InspectorView>());       break;
            }

            DebugHub.Log($"BUTTON_CLICK: {name}");
        }

        private T GetView<T>() where T : UserControl, new()
        {
            string key = typeof(T).Name;
            if (!_viewCache.TryGetValue(key, out var view))
            {
                view = new T();
                _viewCache[key] = view;
            }
            return (T)view;
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
            _service.Disconnect();
            DebugHub.Log("SETTINGS_SAVED_ON_CLOSE");
        }
    }
}