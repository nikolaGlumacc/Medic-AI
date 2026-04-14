using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MedicAIGUI.Services
{
    public class SavedSettings : INotifyPropertyChanged
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MedicAI", "settings.json");

        // Connection
        public string BotIp { get; set; } = "127.0.0.1";
        public int BotPort { get; set; } = 8766;

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
        public double MouseSpeed { get; set; } = 0.7;
        public int DeadzonePx { get; set; } = 8;
        public int MaxMovePx { get; set; } = 12;
        public int MaxDist { get; set; } = 450;
        public double AimLeadFactor { get; set; } = 0.15;

        // PID
        public double PidKp { get; set; } = 0.15;
        public double PidKi { get; set; } = 0.01;
        public double PidKd { get; set; } = 0.05;

        // Follow
        public int FollowFwdThresh { get; set; } = 180;
        public int FollowBackThresh { get; set; } = 80;
        public int FollowStrafeThresh { get; set; } = 100;

        // Tracking
        public int TrackMinHits { get; set; } = 3;
        public int TrackMaxLost { get; set; } = 5;
        public double LockDuration { get; set; } = 2.0;

        // HSV
        public int BluHMin { get; set; } = 95;
        public int BluHMax { get; set; } = 125;
        public int BluSMin { get; set; } = 70;
        public int BluSMax { get; set; } = 255;
        public int BluVMin { get; set; } = 80;
        public int BluVMax { get; set; } = 255;
        public int Red1HMin { get; set; } = 0;
        public int Red1HMax { get; set; } = 8;
        public int Red2HMin { get; set; } = 172;
        public int Red2HMax { get; set; } = 179;

        // Behavior
        public int RetreatHealthThreshold { get; set; } = 50;
        public int UberPopThreshold { get; set; } = 95;
        public bool AutoUber { get; set; } = true;
        public bool PreferMeleeForRetreat { get; set; } = false;
        public bool FollowEnabled { get; set; } = true;

        // Legacy
        public double Smoothing { get; set; } = 0.12;
        public int Deadzone { get; set; } = 30;
        public int MatchDist { get; set; } = 160;
        public double GracePeriod { get; set; } = 1.5;
        public double LockGrace { get; set; } = 0.8;
        public int MaxTrackedPlayers { get; set; } = 8;
        public double PowerClassWeight { get; set; } = 0.5;
        public int SpyCheckFrequency { get; set; } = 8;
        public List<string> PriorityList { get; set; } = new();

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
            BotPort = other.BotPort;
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
            PriorityList = new List<string>(other.PriorityList);
        }
    }
}