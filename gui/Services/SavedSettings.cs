using System;
using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace MedicAIGUI.Services
{
    public class SavedSettings : INotifyPropertyChanged
    {
        private static readonly string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            OnPropertyChanged(name);
        }

        // ── Aim & Mouse ───────────────────────────────────────────────────────
        private double _mouseSpeed = 0.6;
        public double MouseSpeed { get => _mouseSpeed; set => Set(ref _mouseSpeed, value); }

        private double _smoothing = 0.12;
        public double Smoothing { get => _smoothing; set => Set(ref _smoothing, value); }

        private int _deadzone = 30;
        public int Deadzone { get => _deadzone; set => Set(ref _deadzone, value); }

        private int _maxMovePx = 10;
        public int MaxMovePx { get => _maxMovePx; set => Set(ref _maxMovePx, value); }

        private double _aimYMinPct = 15;
        public double AimYMinPct { get => _aimYMinPct; set => Set(ref _aimYMinPct, value); }

        private double _aimYMaxPct = 85;
        public double AimYMaxPct { get => _aimYMaxPct; set => Set(ref _aimYMaxPct, value); }

        private int _maxDist = 450;
        public int MaxDist { get => _maxDist; set => Set(ref _maxDist, value); }

        private double _horizontalSmoothing = 0.12;
        public double HorizontalSmoothing { get => _horizontalSmoothing; set => Set(ref _horizontalSmoothing, value); }

        private double _verticalSmoothing = 0.12;
        public double VerticalSmoothing { get => _verticalSmoothing; set => Set(ref _verticalSmoothing, value); }

        private double _aimVerticalOffsetPct = 0;
        public double AimVerticalOffsetPct { get => _aimVerticalOffsetPct; set => Set(ref _aimVerticalOffsetPct, value); }

        // ── Follow & Movement ────────────────────────────────────────────────
        private int _followFwdThresh = 180;
        public int FollowFwdThresh { get => _followFwdThresh; set => Set(ref _followFwdThresh, value); }

        private int _followBackThresh = 80;
        public int FollowBackThresh { get => _followBackThresh; set => Set(ref _followBackThresh, value); }

        private int _followStrafeThresh = 100;
        public int FollowStrafeThresh { get => _followStrafeThresh; set => Set(ref _followStrafeThresh, value); }

        private bool _followEnabled = true;
        public bool FollowEnabled { get => _followEnabled; set => Set(ref _followEnabled, value); }

        private bool _strafeRandomize = false;
        public bool StrafeRandomize { get => _strafeRandomize; set => Set(ref _strafeRandomize, value); }

        private double _backpedalMaxDuration = 2.0;
        public double BackpedalMaxDuration { get => _backpedalMaxDuration; set => Set(ref _backpedalMaxDuration, value); }

        // ── Tracking ─────────────────────────────────
        private int _matchDist = 160;
        public int MatchDist { get => _matchDist; set => Set(ref _matchDist, value); }

        private double _gracePeriod = 1.5;
        public double GracePeriod { get => _gracePeriod; set => Set(ref _gracePeriod, value); }

        private double _lockGrace = 0.8;
        public double LockGrace { get => _lockGrace; set => Set(ref _lockGrace, value); }

        private int _maxTrackedPlayers = 8;
        public int MaxTrackedPlayers { get => _maxTrackedPlayers; set => Set(ref _maxTrackedPlayers, value); }

        // ── Color Detection (HSV) ─────────────────────
        public int BluHMin { get; set; } = 95;
        public int BluHMax { get; set; } = 125;
        public int BluSMin { get; set; } = 70;
        public int BluSMax { get; set; } = 255;
        public int BluVMin { get; set; } = 80;
        public int BluVMax { get; set; } = 255;
        public int Red1HMin { get; set; } = 0;
        public int Red1HMax { get; set; } = 8;
        public int Red1SMin { get; set; } = 90;
        public int Red1SMax { get; set; } = 255;
        public int Red1VMin { get; set; } = 90;
        public int Red1VMax { get; set; } = 255;
        public int Red2HMin { get; set; } = 172;
        public int Red2HMax { get; set; } = 179;
        public int Red2SMin { get; set; } = 90;
        public int Red2SMax { get; set; } = 255;
        public int Red2VMin { get; set; } = 90;
        public int Red2VMax { get; set; } = 255;

        // ── Blob Filters ──────────────────────────────
        private int _minArea = 600;
        public int MinArea { get => _minArea; set => Set(ref _minArea, value); }
        private double _maxAspect = 5.0;
        public double MaxAspect { get => _maxAspect; set => Set(ref _maxAspect, value); }

        // ── Loadout Switcher ──────────────────────────
        private string _primaryWeapon = "crusaders_crossbow";
        public string PrimaryWeapon { get => _primaryWeapon; set => Set(ref _primaryWeapon, value); }
        private double _matchThreshold = 0.70;
        public double MatchThreshold { get => _matchThreshold; set => Set(ref _matchThreshold, value); }
        private string _scaleFactors = "0.8, 0.9, 1.0, 1.1";
        public string ScaleFactors { get => _scaleFactors; set => Set(ref _scaleFactors, value); }

        // ── UI Customization (Ultimate) ───────────────
        private string _accentColor = "#4ED9FF";
        public string AccentColor { get => _accentColor; set => Set(ref _accentColor, value); }
        
        private double _uiOpacity = 0.92;
        public double UiOpacity { get => _uiOpacity; set => Set(ref _uiOpacity, value); }

        private bool _showGlassEffects = true;
        public bool ShowGlassEffects { get => _showGlassEffects; set => Set(ref _showGlassEffects, value); }

        private string _globalFontFamily = "Segoe UI Variable";
        public string GlobalFontFamily { get => _globalFontFamily; set => Set(ref _globalFontFamily, value); }

        // ── Priority Engine ───────────────────────────
        private double _powerClassWeight = 2.0;
        public double PowerClassWeight { get => _powerClassWeight; set => Set(ref _powerClassWeight, value); }

        private string _priorityList = "";
        public string PriorityList { get => _priorityList; set => Set(ref _priorityList, value); }
        
        private bool _roundOcrEnabled = true;
        public bool RoundOcrEnabled { get => _roundOcrEnabled; set => Set(ref _roundOcrEnabled, value); }

        // Persistence Logic
        
        // ── Bot Brain Thresholds ──────────────────────
        private int _retreatHealthThreshold = 50;
        public int RetreatHealthThreshold { get => _retreatHealthThreshold; set => Set(ref _retreatHealthThreshold, value); }
        
        private int _defendEnemyDistance = 300;
        public int DefendEnemyDistance { get => _defendEnemyDistance; set => Set(ref _defendEnemyDistance, value); }
        
        private double _uberPopThreshold = 95.0;
        public double UberPopThreshold { get => _uberPopThreshold; set => Set(ref _uberPopThreshold, value); }
        
        private bool _preferMeleeForRetreat = true;
        public bool PreferMeleeForRetreat { get => _preferMeleeForRetreat; set => Set(ref _preferMeleeForRetreat, value); }
        
        private bool _autoHeal = true;
        public bool AutoHeal { get => _autoHeal; set => Set(ref _autoHeal, value); }
        
        private bool _autoUber = true;
        public bool AutoUber { get => _autoUber; set => Set(ref _autoUber, value); }
        
        private bool _priorityOnlyHeal = true;
        public bool PriorityOnlyHeal { get => _priorityOnlyHeal; set => Set(ref _priorityOnlyHeal, value); }
        
        private int _followDistance = 50;
        public int FollowDistance { get => _followDistance; set => Set(ref _followDistance, value); }
        
        private int _spyCheckFrequency = 8;
        public int SpyCheckFrequency { get => _spyCheckFrequency; set => Set(ref _spyCheckFrequency, value); }
        
        private int _spyAlertnessDuration = 18;
        public int SpyAlertnessDuration { get => _spyAlertnessDuration; set => Set(ref _spyAlertnessDuration, value); }
        
        private int _spyCheckCameraFlickSpeed = 180;
        public int SpyCheckCameraFlickSpeed { get => _spyCheckCameraFlickSpeed; set => Set(ref _spyCheckCameraFlickSpeed, value); }
        
        private int _idleRotationSpeed = 18;
        public int IdleRotationSpeed { get => _idleRotationSpeed; set => Set(ref _idleRotationSpeed, value); }
        
        private int _scoreboardCheckFrequency = 30;
        public int ScoreboardCheckFrequency { get => _scoreboardCheckFrequency; set => Set(ref _scoreboardCheckFrequency, value); }
        
        private double _tabHoldDuration = 0.25;
        public double TabHoldDuration { get => _tabHoldDuration; set => Set(ref _tabHoldDuration, value); }
        
        private bool _autoWhitelistDetection = true;
        public bool AutoWhitelistDetection { get => _autoWhitelistDetection; set => Set(ref _autoWhitelistDetection, value); }
        
        private int _cpuThrottleThreshold = 85;
        public int CpuThrottleThreshold { get => _cpuThrottleThreshold; set => Set(ref _cpuThrottleThreshold, value); }
        
        private int _screenCaptureFpsLimit = 60;
        public int ScreenCaptureFpsLimit { get => _screenCaptureFpsLimit; set => Set(ref _screenCaptureFpsLimit, value); }

        public static SavedSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonConvert.DeserializeObject<SavedSettings>(json) ?? new SavedSettings();
                }
            } catch { }
            return new SavedSettings();
        }

        public void SaveSettings()
        {
            try {
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
            } catch { }
        }
    }
}
