using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace MedicAIGUI.Services
{
    public class SavedSettings : INotifyPropertyChanged
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MedicAI", "settings.json");

        // Connection
        public string BotIp { get; set; } = "127.0.0.1";
        public int WsPort { get; set; } = 8766;
        public int FlaskPort { get; set; } = 5000;

        // UI
        public string AccentColor { get; set; } = "#4ED9FF";
        public double UiOpacity { get; set; } = 0.95;
        public string GlobalFontFamily { get; set; } = "Segoe UI Variable Display";

        // Window
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public int WindowLeft { get; set; }
        public int WindowTop { get; set; }

        // Aiming
        [JsonPropertyName("mouse_speed")] public double MouseSpeed { get; set; } = 0.7;
        [JsonPropertyName("deadzone_px")] public int DeadzonePx { get; set; } = 8;
        [JsonPropertyName("max_move_px")] public int MaxMovePx { get; set; } = 12;
        [JsonPropertyName("max_dist")] public int MaxDist { get; set; } = 450;
        [JsonPropertyName("aim_lead_factor")] public double AimLeadFactor { get; set; } = 0.15;

        // PID
        [JsonPropertyName("pid_kp")] public double PidKp { get; set; } = 0.15;
        [JsonPropertyName("pid_ki")] public double PidKi { get; set; } = 0.01;
        [JsonPropertyName("pid_kd")] public double PidKd { get; set; } = 0.05;

        // Follow
        [JsonPropertyName("follow_fwd_thresh")] public int FollowFwdThresh { get; set; } = 180;
        [JsonPropertyName("follow_back_thresh")] public int FollowBackThresh { get; set; } = 80;
        [JsonPropertyName("follow_strafe_thresh")] public int FollowStrafeThresh { get; set; } = 100;

        // Tracking
        [JsonPropertyName("track_min_hits")] public int TrackMinHits { get; set; } = 3;
        [JsonPropertyName("track_max_lost")] public int TrackMaxLost { get; set; } = 5;
        [JsonPropertyName("lock_duration")] public double LockDuration { get; set; } = 2.0;

        // HSV (Backing fields for preview logic)
        private int _bluHMin = 95, _bluHMax = 125, _bluSMin = 70, _bluSMax = 255, _bluVMin = 80, _bluVMax = 255;
        private int _red1HMin = 0, _red1HMax = 8, _red2HMin = 172, _red2HMax = 179;

        [JsonPropertyName("blu_h_min")] public int BluHMin { get => _bluHMin; set { _bluHMin = value; OnPropertyChanged(); OnPropertyChanged(nameof(BluPreviewBrush)); } }
        [JsonPropertyName("blu_h_max")] public int BluHMax { get => _bluHMax; set { _bluHMax = value; OnPropertyChanged(); OnPropertyChanged(nameof(BluPreviewBrush)); } }
        [JsonPropertyName("blu_s_min")] public int BluSMin { get => _bluSMin; set { _bluSMin = value; OnPropertyChanged(); OnPropertyChanged(nameof(BluPreviewBrush)); } }
        [JsonPropertyName("blu_s_max")] public int BluSMax { get => _bluSMax; set { _bluSMax = value; OnPropertyChanged(); OnPropertyChanged(nameof(BluPreviewBrush)); } }
        [JsonPropertyName("blu_v_min")] public int BluVMin { get => _bluVMin; set { _bluVMin = value; OnPropertyChanged(); OnPropertyChanged(nameof(BluPreviewBrush)); } }
        [JsonPropertyName("blu_v_max")] public int BluVMax { get => _bluVMax; set { _bluVMax = value; OnPropertyChanged(); OnPropertyChanged(nameof(BluPreviewBrush)); } }
        
        [JsonPropertyName("red1_h_min")] public int Red1HMin { get => _red1HMin; set { _red1HMin = value; OnPropertyChanged(); OnPropertyChanged(nameof(RedPreviewBrush)); } }
        [JsonPropertyName("red1_h_max")] public int Red1HMax { get => _red1HMax; set { _red1HMax = value; OnPropertyChanged(); OnPropertyChanged(nameof(RedPreviewBrush)); } }
        [JsonPropertyName("red2_h_min")] public int Red2HMin { get => _red2HMin; set { _red2HMin = value; OnPropertyChanged(); OnPropertyChanged(nameof(RedPreviewBrush)); } }
        [JsonPropertyName("red2_h_max")] public int Red2HMax { get => _red2HMax; set { _red2HMax = value; OnPropertyChanged(); OnPropertyChanged(nameof(RedPreviewBrush)); } }

        [JsonIgnore]
        public Brush BluPreviewBrush => GetGradientBrush(BluHMin, BluHMax, BluSMin, BluSMax, BluVMin, BluVMax);
        
        [JsonIgnore]
        public Brush RedPreviewBrush => GetGradientBrush(Red1HMin, Red1HMax, 150, 255, 150, 255); // Red usually has high S/V

        private Brush GetGradientBrush(int hMin, int hMax, int sMin, int sMax, int vMin, int vMax)
        {
            return new LinearGradientBrush(
                HsvToRgb(hMin, sMin, vMin),
                HsvToRgb(hMax, sMax, vMax),
                new System.Windows.Point(0, 0),
                new System.Windows.Point(1, 1));
        }

        private Color HsvToRgb(double h, double s, double v)
        {
            // Normalize: H[0,179]->[0,360], S[0,255]->[0,1], V[0,255]->[0,1]
            h = (h * 2) % 360;
            double sNorm = s / 255.0;
            double vNorm = v / 255.0;

            double c = vNorm * sNorm;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = vNorm - c;

            double r1 = 0, g1 = 0, b1 = 0;
            if (h >= 0 && h < 60) { r1 = c; g1 = x; b1 = 0; }
            else if (h >= 60 && h < 120) { r1 = x; g1 = c; b1 = 0; }
            else if (h >= 120 && h < 180) { r1 = 0; g1 = c; b1 = x; }
            else if (h >= 180 && h < 240) { r1 = 0; g1 = x; b1 = c; }
            else if (h >= 240 && h < 300) { r1 = x; g1 = 0; b1 = c; }
            else if (h >= 300 && h < 360) { r1 = c; g1 = 0; b1 = x; }

            return Color.FromRgb((byte)((r1 + m) * 255), (byte)((g1 + m) * 255), (byte)((b1 + m) * 255));
        }

        // Behavior
        [JsonPropertyName("retreat_health_threshold")] public int RetreatHealthThreshold { get; set; } = 50;
        [JsonPropertyName("uber_pop_threshold")] public int UberPopThreshold { get; set; } = 95;
        [JsonPropertyName("auto_uber")] public bool AutoUber { get; set; } = true;
        [JsonPropertyName("prefer_melee_for_retreat")] public bool PreferMeleeForRetreat { get; set; } = false;
        [JsonPropertyName("follow_enabled")] public bool FollowEnabled { get; set; } = true;

        // Legacy
        [JsonPropertyName("smoothing")] public double Smoothing { get; set; } = 0.12;
        [JsonPropertyName("deadzone")] public int Deadzone { get; set; } = 30;
        [JsonPropertyName("match_dist")] public int MatchDist { get; set; } = 160;
        [JsonPropertyName("grace_period")] public double GracePeriod { get; set; } = 1.5;
        [JsonPropertyName("lock_grace")] public double LockGrace { get; set; } = 0.8;
        [JsonPropertyName("max_tracked_players")] public int MaxTrackedPlayers { get; set; } = 8;
        [JsonPropertyName("power_class_weight")] public double PowerClassWeight { get; set; } = 0.5;
        [JsonPropertyName("spy_check_frequency")] public int SpyCheckFrequency { get; set; } = 8;
        [JsonPropertyName("current_target")] public string CurrentTarget { get; set; } = "";

        // TuningView bindings (referenced in TuningView.xaml)
        [JsonPropertyName("strafe_randomize")] public bool StrafeRandomize { get; set; } = false;
        [JsonPropertyName("idle_rotation_speed")] public int IdleRotationSpeed { get; set; } = 18;
        [JsonPropertyName("vaccinator_overlay_x")] public double VaccinatorOverlayX { get; set; } = 0;
        [JsonPropertyName("vaccinator_overlay_y")] public double VaccinatorOverlayY { get; set; } = 0;

        // Loadout
        public string PrimaryWeapon { get; set; } = "Crusader's Crossbow";
        public string SecondaryWeapon { get; set; } = "Medi Gun";
        public string MeleeWeapon { get; set; } = "Ubersaw";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public static SavedSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonSerializer.Deserialize<SavedSettings>(File.ReadAllText(SettingsPath)) ?? new();
            }
            catch { }
            return new();
        }

        public void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public void SaveToPath(string path) =>
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

        public static SavedSettings LoadFromPath(string path)
        {
            try { return JsonSerializer.Deserialize<SavedSettings>(File.ReadAllText(path)) ?? new(); }
            catch { return new(); }
        }

        public void ApplyFrom(SavedSettings other)
        {
            BotIp = other.BotIp;
            WsPort = other.WsPort;
            FlaskPort = other.FlaskPort;
            AccentColor = other.AccentColor;
            UiOpacity = other.UiOpacity;
            GlobalFontFamily = other.GlobalFontFamily;
            MouseSpeed = other.MouseSpeed;
            DeadzonePx = other.DeadzonePx;
            MaxMovePx = other.MaxMovePx;
            MaxDist = other.MaxDist;
            AimLeadFactor = other.AimLeadFactor;
            PidKp = other.PidKp;
            PidKi = other.PidKi;
            PidKd = other.PidKd;
            FollowFwdThresh = other.FollowFwdThresh;
            FollowBackThresh = other.FollowBackThresh;
            FollowStrafeThresh = other.FollowStrafeThresh;
            TrackMinHits = other.TrackMinHits;
            TrackMaxLost = other.TrackMaxLost;
            LockDuration = other.LockDuration;
            BluHMin = other.BluHMin; BluHMax = other.BluHMax;
            BluSMin = other.BluSMin; BluSMax = other.BluSMax;
            BluVMin = other.BluVMin; BluVMax = other.BluVMax;
            Red1HMin = other.Red1HMin; Red1HMax = other.Red1HMax;
            Red2HMin = other.Red2HMin; Red2HMax = other.Red2HMax;
            RetreatHealthThreshold = other.RetreatHealthThreshold;
            UberPopThreshold = other.UberPopThreshold;
            AutoUber = other.AutoUber;
            PreferMeleeForRetreat = other.PreferMeleeForRetreat;
            FollowEnabled = other.FollowEnabled;
            Smoothing = other.Smoothing;
            Deadzone = other.Deadzone;
            MatchDist = other.MatchDist;
            GracePeriod = other.GracePeriod;
            LockGrace = other.LockGrace;
            MaxTrackedPlayers = other.MaxTrackedPlayers;
            PowerClassWeight = other.PowerClassWeight;
            SpyCheckFrequency = other.SpyCheckFrequency;
            CurrentTarget = other.CurrentTarget;
            VaccinatorOverlayX = other.VaccinatorOverlayX;
            VaccinatorOverlayY = other.VaccinatorOverlayY;
            StrafeRandomize = other.StrafeRandomize;
            IdleRotationSpeed = other.IdleRotationSpeed;
            PrimaryWeapon = other.PrimaryWeapon;
            SecondaryWeapon = other.SecondaryWeapon;
            MeleeWeapon = other.MeleeWeapon;
        }
    }
}