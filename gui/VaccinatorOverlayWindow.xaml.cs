using System;
using System.Windows;
using System.Windows.Input;
using MedicAIGUI.Services;

namespace MedicAIGUI
{
    public partial class VaccinatorOverlayWindow : Window
    {
        private static VaccinatorOverlayWindow _instance;
        public static VaccinatorOverlayWindow Instance => _instance ??= new VaccinatorOverlayWindow();

        private MedicBotService _service = MedicBotService.Instance;
        private Hotkey _cycleKey;
        private Hotkey _uberKey;

        private VaccinatorOverlayWindow()
        {
            InitializeComponent();
            _service.OnVaccinatorResistChanged += OnResistChanged;
            Loaded += (s, e) => RegisterHotkeys();
        }

        private void RegisterHotkeys()
        {
            var settings = SavedSettings.Load();
            // Cycle key (default R)
            var cycleKey = (Key)Enum.Parse(typeof(Key), settings.VaccinatorCycleKey ?? "R");
            _cycleKey = new Hotkey(cycleKey, KeyModifier.None);
            _cycleKey.HotkeyPressed += async (s, e) => await _service.CycleVaccinator();
            _cycleKey.Register();

            // Uber key (default M2 = Right mouse button)
            var uberKey = (Key)Enum.Parse(typeof(Key), settings.UberKeybind ?? "M");
            _uberKey = new Hotkey(uberKey, KeyModifier.None);
            _uberKey.HotkeyPressed += async (s, e) => await _service.PopUber();
            _uberKey.Register();
        }

        private void OnResistChanged(string resist)
        {
            Dispatcher.Invoke(() =>
            {
                string icon = resist switch
                {
                    "bullet" => "pack://application:,,,/Resources/vaccinator_ammo1.png",
                    "explosive" => "pack://application:,,,/Resources/vaccinator_bomb1.png",
                    "fire" => "pack://application:,,,/Resources/vaccinator_fire2.png",
                    _ => "pack://application:,,,/Resources/vaccinator_default.png"
                };
                ResistIcon.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(icon));
                ResistText.Text = resist.ToUpper();
            });
        }
    }
}