using System;
using System.Windows;
using System.Windows.Media.Imaging;
using MedicAIGUI.Services;
using System.IO;

namespace MedicAIGUI.Views
{
    public partial class VaccinatorOverlayWindow : Window
    {
        private static VaccinatorOverlayWindow? _instance;
        public static VaccinatorOverlayWindow Instance
        {
            get
            {
                if (_instance == null) _instance = new VaccinatorOverlayWindow();
                return _instance;
            }
        }

        private VaccinatorOverlayWindow()
        {
            InitializeComponent();
            MedicBotService.Instance.OnVaccinatorResistChanged += OnResistChanged;
            MedicBotService.Instance.Settings.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "VaccinatorOverlayX" || e.PropertyName == "VaccinatorOverlayY")
                {
                    Dispatcher.Invoke(UpdatePosition);
                }
            };
            UpdatePosition();
        }

        public void UpdatePosition()
        {
            var settings = MedicBotService.Instance.Settings;
            // Center is screen width/height over 2, plus customized offset
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            
            // X and Y from settings are raw screen coordinates. 
            // If they are 0, default to below center.
            double x = settings.VaccinatorOverlayX;
            double y = settings.VaccinatorOverlayY;

            if (x == 0 && y == 0)
            {
                x = (screenWidth / 2) - 32;
                y = (screenHeight / 2) + 100; // a bit below center
            }

            Left = x;
            Top = y;
        }

        private void OnResistChanged(string resist)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", $"vaccinator_{resist}.png");
                    
                    // Fallback to absolute project path if running locally
                    if (!File.Exists(path))
                    {
                        var basePath = AppDomain.CurrentDomain.BaseDirectory;
                        path = Path.Combine(basePath, "..", "..", "..", "gui", "Resources", $"vaccinator_{resist}.png");
                    }
                    path = Path.GetFullPath(path);

                    if (File.Exists(path))
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(path, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        OverlayImage.Source = bmp;
                    }
                }
                catch (Exception ex)
                {
                    DebugHub.Log("Overlay image load error: " + ex.Message);
                }
            });
        }
        
        // Prevent window from actually closing, just hide it
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }
    }
}
