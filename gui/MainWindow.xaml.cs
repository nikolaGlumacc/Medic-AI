using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace MedicAIGUI
{
    // ══════════════════════════════════════════════════════════════════════════
    // Settings persistence data contract
    // ══════════════════════════════════════════════════════════════════════════

    public class SavedSettings
    {
        // Connection
        public string BotIp { get; set; } = "127.0.0.1";
        public string Port { get; set; } = "8765";
        public bool AutoReconnect { get; set; } = true;
        public double ConnectionTimeout { get; set; } = 6;

        // Follow behavior
        public double FollowDistance { get; set; } = 50;
        public double ActiveModeFollowDistance { get; set; } = 60;
        public double PassiveModeFollowDistance { get; set; } = 80;
        public double EscortModeFollowDistance { get; set; } = 45;
        public double ErraticMovementThreshold { get; set; } = 45;
        public double SearchModeTimeout { get; set; } = 15;
        public double StuckDetectionSensitivity { get; set; } = 55;
        public double StuckDetectionRetry { get; set; } = 3;
        public double LedgeCorrectionSensitivity { get; set; } = 40;

        // Uber
        public bool AutoPop { get; set; } = true;
        public bool ManualOnly { get; set; } = false;
        public bool UberSuggestionAudio { get; set; } = true;
        public bool HoldUberIfPriorityDead { get; set; } = true;
        public bool DefensiveUber { get; set; } = true;
        public double AutoPopHealthThreshold { get; set; } = 30;

        // Spy detection
        public double SpyCheckFreq { get; set; } = 8;
        public double SpyAlertnessDuration { get; set; } = 18;
        public bool SpyAlertnessAfterKillFeed { get; set; } = true;
        public double SpyCameraFlickSpeed { get; set; } = 180;

        // Scanning
        public double IdleRotationSpeed { get; set; } = 18;
        public double CalloutRotationSpeed { get; set; } = 45;
        public double RotationDegreesPerStep { get; set; } = 15;
        public double YoloConfidenceThreshold { get; set; } = 60;
        public double OcrSensitivity { get; set; } = 55;
        public double SearchModeRotationSpeed { get; set; } = 28;

        // Melee
        public bool MeleeEnabled { get; set; } = true;
        public double OutnumberedRetreatThreshold { get; set; } = 2;
        public double CriticalHealthRetreatThreshold { get; set; } = 20;
        public bool MeleeKillVoiceLine { get; set; } = true;
        public bool MeleeKillLogging { get; set; } = true;

        // Passive mode
        public int DefaultModeOnStartupIndex { get; set; } = 0;
        public double DoubleCalloutTimeout { get; set; } = 1.2;
        public double PassiveModeIdleRotationSpeed { get; set; } = 8;
        public bool PassiveModeEnemyAvoidance { get; set; } = true;
        public bool PassiveIdleAudioCue { get; set; } = false;
        public double PassiveIdleAudioCueInterval { get; set; } = 25;

        // Audio
        public double MasterVolume { get; set; } = 75;
        public bool MasterVoiceLine { get; set; } = true;

        // Scoreboard
        public double ScoreboardCheckFrequency { get; set; } = 30;
        public double TabHoldDuration { get; set; } = 0.25;
        public bool AutoUpdateWhitelistFromScoreboard { get; set; } = true;
        public bool AutoWhitelistDetection { get; set; } = true;
        public bool ClassDetection { get; set; } = true;

        // Session & logging
        public bool AutoExportLog { get; set; } = true;
        public string LogFileSaveLocation { get; set; } = "";
        public bool WeeklySummary { get; set; } = false;
        public int WeeklySummaryDayIndex { get; set; } = 0;
        public bool TimestampsInLog { get; set; } = true;
        public bool DisconnectKickLogging { get; set; } = true;

        // Performance
        public double ScreenCaptureFpsLimit { get; set; } = 60;
        public int YoloThreadPriorityIndex { get; set; } = 1;
        public double CpuThrottleThreshold { get; set; } = 85;
        public double ThrottleAmount { get; set; } = 40;
        public bool WatchdogAutoRestart { get; set; } = true;
        public bool Tf2WindowLock { get; set; } = true;

        // Startup & shutdown
        public bool AutoSkipTf2Intro { get; set; } = true;
        public bool AutoSelectMedic { get; set; } = true;
        public bool AutoEquipLoadout { get; set; } = true;
        public int CleanShutdownIndex { get; set; } = 0;
        public double WatchdogRestartDelay { get; set; } = 8;

        // Updates
        public bool AutoCheckUpdatesOnLaunch { get; set; } = false;

        // ── Bot Brain (NEW) ─────────────────────────────────────────────────
        public int    RetreatHealthThreshold  { get; set; } = 50;
        public int    DefendEnemyDistance     { get; set; } = 300;
        public int    UberPopThreshold        { get; set; } = 95;
        public bool   PreferMeleeForRetreat   { get; set; } = true;
        public bool   AutoHealBrain           { get; set; } = true;
        public bool   AutoUberBrain           { get; set; } = true;
        public bool   PriorityOnlyHeal        { get; set; } = true;

        // Priority / whitelist / blacklist
        public List<SavedPriorityPlayer> Priorities { get; set; } = new();
        public List<string> Whitelist { get; set; } = new();
        public List<string> Blacklist { get; set; } = new();

        // Loadout presets
        public List<SavedLoadoutPreset> LoadoutPresets { get; set; } = new();
        public int ActivePresetIndex { get; set; } = 0;

        // Audio cue settings
        public List<SavedAudioCue> AudioCues { get; set; } = new();

        // Voice line settings
        public List<SavedVoiceLine> VoiceLines { get; set; } = new();
    }

    public class SavedPriorityPlayer
    {
        public string Name { get; set; } = "";
        public int Tier { get; set; } = 1;
        public string FollowDistanceOverride { get; set; } = "0";
        public string PassiveModeOverride { get; set; } = "Inherit";
    }

    public class SavedLoadoutPreset
    {
        public string Name { get; set; } = "";
        public string PrimaryWeapon { get; set; } = "Crusader's Crossbow";
        public string SecondaryWeapon { get; set; } = "Medi Gun";
        public string MeleeWeapon { get; set; } = "Ubersaw";
    }

    public class SavedAudioCue
    {
        public string Key { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public int Volume { get; set; } = 75;
        public string CustomFilePath { get; set; } = "";
    }

    public class SavedVoiceLine
    {
        public string Key { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public int Volume { get; set; } = 75;
        public string CustomFilePath { get; set; } = "";
        public bool TtsFallback { get; set; } = true;
        public int TtsVolume { get; set; } = 70;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Bindable base + data models
    // ══════════════════════════════════════════════════════════════════════════

    public abstract class BindableBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class PriorityPlayer : BindableBase
    {
        private string _statusIcon = "T1";
        private string _name = string.Empty;
        private int _tier = 1;
        private int _deaths;
        private string _followDistanceOverride = "0";
        private string _passiveModeOverride = "Inherit";

        public string StatusIcon { get => _statusIcon; set => SetProperty(ref _statusIcon, value); }
        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public int Tier
        {
            get => _tier;
            set { if (SetProperty(ref _tier, value)) StatusIcon = $"T{Math.Clamp(value, 1, 9)}"; }
        }
        public int Deaths { get => _deaths; set => SetProperty(ref _deaths, value); }
        public string FollowDistanceOverride { get => _followDistanceOverride; set => SetProperty(ref _followDistanceOverride, value); }
        public string PassiveModeOverride { get => _passiveModeOverride; set => SetProperty(ref _passiveModeOverride, value); }
    }

    public class RespawnTimerObj
    {
        public string DisplayText { get; set; } = string.Empty;
        public SolidColorBrush Color { get; set; } = Brushes.Transparent;
    }

    public class LoadoutPreset : BindableBase
    {
        private string _name = string.Empty;
        private string _primaryWeapon = "Crusader's Crossbow";
        private string _secondaryWeapon = "Medi Gun";
        private string _meleeWeapon = "Ubersaw";

        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public string PrimaryWeapon { get => _primaryWeapon; set => SetProperty(ref _primaryWeapon, value); }
        public string SecondaryWeapon { get => _secondaryWeapon; set => SetProperty(ref _secondaryWeapon, value); }
        public string MeleeWeapon { get => _meleeWeapon; set => SetProperty(ref _meleeWeapon, value); }
    }

    public class AutoLoadoutRule : BindableBase
    {
        private string _teammateClass = "Soldier";
        private LoadoutPreset? _preset;
        public string TeammateClass { get => _teammateClass; set => SetProperty(ref _teammateClass, value); }
        public LoadoutPreset? Preset { get => _preset; set => SetProperty(ref _preset, value); }
    }

    public class AudioCueSetting : BindableBase
    {
        private bool _enabled = true;
        private int _volume = 75;
        private string _customFilePath = string.Empty;

        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
        public int Volume { get => _volume; set => SetProperty(ref _volume, value); }
        public string CustomFilePath { get => _customFilePath; set => SetProperty(ref _customFilePath, value); }
    }

    public class VoiceLineSetting : BindableBase
    {
        private bool _enabled = true;
        private int _volume = 75;
        private string _customFilePath = string.Empty;
        private bool _ttsFallbackEnabled = true;
        private int _ttsVolume = 70;

        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
        public int Volume { get => _volume; set => SetProperty(ref _volume, value); }
        public string CustomFilePath { get => _customFilePath; set => SetProperty(ref _customFilePath, value); }
        public bool TtsFallbackEnabled { get => _ttsFallbackEnabled; set => SetProperty(ref _ttsFallbackEnabled, value); }
        public int TtsVolume { get => _ttsVolume; set => SetProperty(ref _ttsVolume, value); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Main window
    // ══════════════════════════════════════════════════════════════════════════

    public partial class MainWindow : Window
    {
        private sealed class ProcessResult
        {
            public int ExitCode { get; init; }
            public string StandardOutput { get; init; } = string.Empty;
            public string StandardError { get; init; } = string.Empty;
            public bool Succeeded => ExitCode == 0;
            public string CombinedOutput
            {
                get
                {
                    if (string.IsNullOrWhiteSpace(StandardOutput)) return StandardError.Trim();
                    if (string.IsNullOrWhiteSpace(StandardError)) return StandardOutput.Trim();
                    return $"{StandardOutput.Trim()}{Environment.NewLine}{StandardError.Trim()}";
                }
            }
        }

        private sealed class UpdateSnapshot
        {
            public bool IsRepositoryAvailable { get; init; }
            public string Branch { get; init; } = "local";
            public string Commit { get; init; } = "unknown";
            public bool WorktreeDirty { get; init; }
            public int AheadCount { get; init; }
            public int BehindCount { get; init; }
            public string Summary { get; init; } = "Update status unavailable";
            public string Detail { get; init; } = "Run MedicAI from a git clone to enable in-app updating.";
            public string RepoPath { get; init; } = string.Empty;
        }

        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private DispatcherTimer? _reconnectTimer;
        private DispatcherTimer? _sessionTimer;
        private readonly MediaPlayer _previewPlayer = new();

        // Shared HttpClient for brain config sync (reused across calls)
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

        private int _totalHealing;
        private int _meleeKills;
        private int _allTimeMeleeKills;
        private bool _isConnected;
        private bool _isBotRunning;
        private int _sessionSeconds;
        private bool _isInitialized;
        private bool _isUpdateOperationRunning;
        private bool _sessionLogExported;
        private readonly string? _repositoryRoot;

        public ObservableCollection<PriorityPlayer> Priorities { get; } = new();
        public ObservableCollection<RespawnTimerObj> RespawnTimers { get; } = new();
        public ObservableCollection<string> WhitelistEntries { get; } = new();
        public ObservableCollection<string> BlacklistEntries { get; } = new();
        public ObservableCollection<LoadoutPreset> LoadoutPresets { get; } = new();
        public ObservableCollection<AutoLoadoutRule> AutoLoadoutRules { get; } = new();
        public ObservableCollection<AudioCueSetting> AudioCueSettings { get; } = new();
        public ObservableCollection<VoiceLineSetting> VoiceLineSettings { get; } = new();

        public int[] PriorityTierOptions { get; } = { 1, 2, 3 };
        public string[] PassiveOverrideOptions { get; } = { "Inherit", "Force Passive", "Force Active" };
        public string[] PrimaryWeaponOptions { get; } = { "Syringe Gun (Stock)", "Crusader's Crossbow" };
        public string[] SecondaryWeaponOptions { get; } = { "Medi Gun", "Kritzkrieg", "Quick-Fix", "Vaccinator" };
        public string[] MeleeWeaponOptions { get; } = { "Bonesaw (Stock)", "Vita-Saw", "Ubersaw", "Amputator", "Solemn Vow" };
        public string[] TeammateClassOptions { get; } = { "Scout", "Soldier", "Pyro", "Demoman", "Heavy", "Engineer", "Medic", "Sniper", "Spy" };
        public string[] DefaultModeOptions { get; } = { "Active", "Passive" };
        public string[] ThreadPriorityOptions { get; } = { "Below Normal", "Normal", "Above Normal", "High" };
        public string[] WeekdayOptions { get; } = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        public string[] CleanShutdownOptions { get; } = { "Idle in spawn", "Disconnect immediately" };

        // ── constructor ──────────────────────────────────────────────────────

        public MainWindow()
        {
            InitializeComponent();
            _repositoryRoot = FindRepositoryRoot();

            DataContext = this;

            PriorityListView.ItemsSource       = Priorities;
            RespawnTimerList.ItemsSource       = RespawnTimers;
            WhitelistBox.ItemsSource           = WhitelistEntries;
            BlacklistBox.ItemsSource           = BlacklistEntries;
            LoadoutPresetListView.ItemsSource  = LoadoutPresets;
            AutoLoadoutRuleListView.ItemsSource = AutoLoadoutRules;
            AudioCueListView.ItemsSource       = AudioCueSettings;
            VoiceLineListView.ItemsSource      = VoiceLineSettings;

            InitializeDefaultData();
            LoadSettings();
            LoadOperatorState();
            UpdateAllTimeMeleeCounter();

            SetupTimers();
            ResetSessionState();
            ApplyConnectionState(false, "Offline", Brushes.IndianRed);

            DashboardScroller.Visibility = Visibility.Visible;
            PriorityScroller.Visibility  = Visibility.Collapsed;
            SettingsScroller.Visibility  = Visibility.Collapsed;

            _isInitialized = true;
            _ = RefreshUpdateStatusAsync(fetchRemote: AutoCheckUpdatesOnLaunchToggle.IsChecked == true, logErrors: false);
        }

        // ── settings file path ───────────────────────────────────────────────

        private string GetSettingsFilePath()
        {
            string root    = _repositoryRoot ?? Directory.GetCurrentDirectory();
            string logsDir = Path.Combine(root, "logs");
            Directory.CreateDirectory(logsDir);
            return Path.Combine(logsDir, "gui_settings.json");
        }

        // ── save settings ────────────────────────────────────────────────────

        private void SaveSettings()
        {
            try
            {
                var s = new SavedSettings
                {
                    // Connection
                    BotIp             = BotIpInput.Text.Trim(),
                    Port              = PortInput.Text.Trim(),
                    AutoReconnect     = AutoReconnectCB.IsChecked == true,
                    ConnectionTimeout = ConnectionTimeoutSlider.Value,

                    // Follow behavior
                    FollowDistance                = FollowDistanceSlider.Value,
                    ActiveModeFollowDistance      = ActiveModeFollowDistanceSlider.Value,
                    PassiveModeFollowDistance     = PassiveModeFollowDistanceSlider.Value,
                    EscortModeFollowDistance      = EscortModeFollowDistanceSlider.Value,
                    ErraticMovementThreshold      = ErraticMovementThresholdSlider.Value,
                    SearchModeTimeout             = SearchModeTimeoutSlider.Value,
                    StuckDetectionSensitivity     = StuckDetectionSensitivitySlider.Value,
                    StuckDetectionRetry           = StuckDetectionRetrySlider.Value,
                    LedgeCorrectionSensitivity    = LedgeCorrectionSensitivitySlider.Value,

                    // Uber
                    AutoPop                  = AutoPopToggle.IsChecked == true,
                    ManualOnly               = ManualOnlyToggle.IsChecked == true,
                    UberSuggestionAudio      = UberSuggestionAudioCueToggle.IsChecked == true,
                    HoldUberIfPriorityDead   = HoldUberIfPriorityDeadToggle.IsChecked == true,
                    DefensiveUber            = DefensiveUberToggle.IsChecked == true,
                    AutoPopHealthThreshold   = AutoPopHealthThresholdSlider.Value,

                    // Spy
                    SpyCheckFreq             = SpyCheckFreqSlider.Value,
                    SpyAlertnessDuration     = SpyAlertnessDurationSlider.Value,
                    SpyAlertnessAfterKillFeed = SpyAlertnessIncreaseAfterKillFeedToggle.IsChecked == true,
                    SpyCameraFlickSpeed      = SpyCheckCameraFlickSpeedSlider.Value,

                    // Scanning
                    IdleRotationSpeed        = IdleRotationSpeedSlider.Value,
                    CalloutRotationSpeed     = CalloutRotationSpeedSlider.Value,
                    RotationDegreesPerStep   = RotationDegreesPerStepSlider.Value,
                    YoloConfidenceThreshold  = YoloConfidenceThresholdSlider.Value,
                    OcrSensitivity           = OcrSensitivitySlider.Value,
                    SearchModeRotationSpeed  = SearchModeRotationSpeedSlider.Value,

                    // Melee
                    MeleeEnabled                  = MeleeModeToggle.IsChecked == true,
                    OutnumberedRetreatThreshold   = OutnumberedRetreatThresholdSlider.Value,
                    CriticalHealthRetreatThreshold = CriticalHealthRetreatThresholdSlider.Value,
                    MeleeKillVoiceLine            = MeleeKillVoiceLineToggle.IsChecked == true,
                    MeleeKillLogging              = MeleeKillLoggingToggle.IsChecked == true,

                    // Passive
                    DefaultModeOnStartupIndex    = DefaultModeOnStartupCombo.SelectedIndex,
                    DoubleCalloutTimeout         = DoubleCalloutTimeoutSlider.Value,
                    PassiveModeIdleRotationSpeed = PassiveModeIdleRotationSpeedSlider.Value,
                    PassiveModeEnemyAvoidance    = PassiveModeEnemyAvoidanceToggle.IsChecked == true,
                    PassiveIdleAudioCue          = PassiveIdleAudioCueToggle.IsChecked == true,
                    PassiveIdleAudioCueInterval  = PassiveIdleAudioCueIntervalSlider.Value,

                    // Audio
                    MasterVolume    = MasterVolumeSlider.Value,
                    MasterVoiceLine = MasterVoiceLineToggle.IsChecked == true,

                    // Scoreboard
                    ScoreboardCheckFrequency          = ScoreboardCheckFrequencySlider.Value,
                    TabHoldDuration                   = TabHoldDurationSlider.Value,
                    AutoUpdateWhitelistFromScoreboard = AutoUpdateWhitelistFromScoreboardToggle.IsChecked == true,
                    AutoWhitelistDetection            = AutoWhitelistDetectionCheckBox.IsChecked == true,
                    ClassDetection                    = ClassDetectionToggle.IsChecked == true,

                    // Logging
                    AutoExportLog        = AutoExportLogAfterSessionToggle.IsChecked == true,
                    LogFileSaveLocation  = LogFileSaveLocationInput.Text.Trim(),
                    WeeklySummary        = WeeklySummaryToggle.IsChecked == true,
                    WeeklySummaryDayIndex = WeeklySummaryDayCombo.SelectedIndex,
                    TimestampsInLog      = TimestampsInLogToggle.IsChecked == true,
                    DisconnectKickLogging = DisconnectKickLoggingToggle.IsChecked == true,

                    // Performance
                    ScreenCaptureFpsLimit    = ScreenCaptureFpsLimitSlider.Value,
                    YoloThreadPriorityIndex  = YoloThreadPriorityCombo.SelectedIndex,
                    CpuThrottleThreshold     = CpuThrottleThresholdSlider.Value,
                    ThrottleAmount           = ThrottleAmountSlider.Value,
                    WatchdogAutoRestart      = WatchdogAutoRestartToggle.IsChecked == true,
                    Tf2WindowLock            = Tf2WindowLockToggle.IsChecked == true,

                    // Startup / shutdown
                    AutoSkipTf2Intro   = AutoSkipTf2IntroToggle.IsChecked == true,
                    AutoSelectMedic    = AutoSelectMedicToggle.IsChecked == true,
                    AutoEquipLoadout   = AutoEquipLoadoutOnStartToggle.IsChecked == true,
                    CleanShutdownIndex = CleanShutdownBehaviorCombo.SelectedIndex,
                    WatchdogRestartDelay = WatchdogRestartDelaySlider.Value,

                    // Updates
                    AutoCheckUpdatesOnLaunch = AutoCheckUpdatesOnLaunchToggle.IsChecked == true,

                    // ── Bot Brain (NEW) ──────────────────────────────────────
                    RetreatHealthThreshold = (int)RetreatHealthThresholdSlider.Value,
                    DefendEnemyDistance    = (int)DefendEnemyDistanceSlider.Value,
                    UberPopThreshold       = (int)UberPopThresholdSlider.Value,
                    PreferMeleeForRetreat  = PreferMeleeForRetreatToggle.IsChecked == true,
                    AutoHealBrain          = AutoHealToggle.IsChecked == true,
                    AutoUberBrain          = AutoUberBrainToggle.IsChecked == true,
                    PriorityOnlyHeal       = PriorityOnlyHealToggle.IsChecked == true,

                    // Lists
                    Priorities = Priorities.Select(p => new SavedPriorityPlayer
                    {
                        Name = p.Name, Tier = p.Tier,
                        FollowDistanceOverride = p.FollowDistanceOverride,
                        PassiveModeOverride = p.PassiveModeOverride
                    }).ToList(),
                    Whitelist  = WhitelistEntries.ToList(),
                    Blacklist  = BlacklistEntries.ToList(),

                    LoadoutPresets = LoadoutPresets.Select(p => new SavedLoadoutPreset
                    {
                        Name = p.Name, PrimaryWeapon = p.PrimaryWeapon,
                        SecondaryWeapon = p.SecondaryWeapon, MeleeWeapon = p.MeleeWeapon
                    }).ToList(),
                    ActivePresetIndex = Math.Max(0, ActivePresetSelector.SelectedIndex),

                    AudioCues = AudioCueSettings.Select(a => new SavedAudioCue
                    {
                        Key = a.Key, Enabled = a.Enabled,
                        Volume = a.Volume, CustomFilePath = a.CustomFilePath
                    }).ToList(),

                    VoiceLines = VoiceLineSettings.Select(v => new SavedVoiceLine
                    {
                        Key = v.Key, Enabled = v.Enabled, Volume = v.Volume,
                        CustomFilePath = v.CustomFilePath,
                        TtsFallback = v.TtsFallbackEnabled, TtsVolume = v.TtsVolume
                    }).ToList(),
                };

                File.WriteAllText(GetSettingsFilePath(),
                    JsonConvert.SerializeObject(s, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SaveSettings] {ex.Message}");
            }
        }

        // ── load settings ────────────────────────────────────────────────────

        private void LoadSettings()
        {
            string path = GetSettingsFilePath();
            if (!File.Exists(path)) return;

            SavedSettings? s;
            try { s = JsonConvert.DeserializeObject<SavedSettings>(File.ReadAllText(path)); }
            catch { return; }
            if (s == null) return;

            // Connection
            BotIpInput.Text                   = s.BotIp;
            PortInput.Text                    = s.Port;
            AutoReconnectCB.IsChecked         = s.AutoReconnect;
            ConnectionTimeoutSlider.Value     = s.ConnectionTimeout;

            // Follow
            FollowDistanceSlider.Value              = s.FollowDistance;
            ActiveModeFollowDistanceSlider.Value    = s.ActiveModeFollowDistance;
            PassiveModeFollowDistanceSlider.Value   = s.PassiveModeFollowDistance;
            EscortModeFollowDistanceSlider.Value    = s.EscortModeFollowDistance;
            ErraticMovementThresholdSlider.Value    = s.ErraticMovementThreshold;
            SearchModeTimeoutSlider.Value           = s.SearchModeTimeout;
            StuckDetectionSensitivitySlider.Value   = s.StuckDetectionSensitivity;
            StuckDetectionRetrySlider.Value         = s.StuckDetectionRetry;
            LedgeCorrectionSensitivitySlider.Value  = s.LedgeCorrectionSensitivity;

            // Uber
            AutoPopToggle.IsChecked                    = s.AutoPop;
            ManualOnlyToggle.IsChecked                 = s.ManualOnly;
            UberSuggestionAudioCueToggle.IsChecked     = s.UberSuggestionAudio;
            HoldUberIfPriorityDeadToggle.IsChecked     = s.HoldUberIfPriorityDead;
            DefensiveUberToggle.IsChecked              = s.DefensiveUber;
            AutoPopHealthThresholdSlider.Value         = s.AutoPopHealthThreshold;

            // Spy
            SpyCheckFreqSlider.Value                          = s.SpyCheckFreq;
            SpyAlertnessDurationSlider.Value                  = s.SpyAlertnessDuration;
            SpyAlertnessIncreaseAfterKillFeedToggle.IsChecked = s.SpyAlertnessAfterKillFeed;
            SpyCheckCameraFlickSpeedSlider.Value              = s.SpyCameraFlickSpeed;

            // Scanning
            IdleRotationSpeedSlider.Value       = s.IdleRotationSpeed;
            CalloutRotationSpeedSlider.Value    = s.CalloutRotationSpeed;
            RotationDegreesPerStepSlider.Value  = s.RotationDegreesPerStep;
            YoloConfidenceThresholdSlider.Value = s.YoloConfidenceThreshold;
            OcrSensitivitySlider.Value          = s.OcrSensitivity;
            SearchModeRotationSpeedSlider.Value = s.SearchModeRotationSpeed;

            // Melee
            MeleeModeToggle.IsChecked                    = s.MeleeEnabled;
            OutnumberedRetreatThresholdSlider.Value      = s.OutnumberedRetreatThreshold;
            CriticalHealthRetreatThresholdSlider.Value   = s.CriticalHealthRetreatThreshold;
            MeleeKillVoiceLineToggle.IsChecked           = s.MeleeKillVoiceLine;
            MeleeKillLoggingToggle.IsChecked             = s.MeleeKillLogging;

            // Passive
            DefaultModeOnStartupCombo.SelectedIndex    = s.DefaultModeOnStartupIndex;
            DoubleCalloutTimeoutSlider.Value           = s.DoubleCalloutTimeout;
            PassiveModeIdleRotationSpeedSlider.Value   = s.PassiveModeIdleRotationSpeed;
            PassiveModeEnemyAvoidanceToggle.IsChecked  = s.PassiveModeEnemyAvoidance;
            PassiveIdleAudioCueToggle.IsChecked        = s.PassiveIdleAudioCue;
            PassiveIdleAudioCueIntervalSlider.Value    = s.PassiveIdleAudioCueInterval;

            // Audio
            MasterVolumeSlider.Value          = s.MasterVolume;
            MasterVoiceLineToggle.IsChecked   = s.MasterVoiceLine;

            // Scoreboard
            ScoreboardCheckFrequencySlider.Value              = s.ScoreboardCheckFrequency;
            TabHoldDurationSlider.Value                       = s.TabHoldDuration;
            AutoUpdateWhitelistFromScoreboardToggle.IsChecked = s.AutoUpdateWhitelistFromScoreboard;
            AutoWhitelistDetectionCheckBox.IsChecked          = s.AutoWhitelistDetection;
            ClassDetectionToggle.IsChecked                    = s.ClassDetection;

            // Logging
            AutoExportLogAfterSessionToggle.IsChecked = s.AutoExportLog;
            if (!string.IsNullOrWhiteSpace(s.LogFileSaveLocation))
                LogFileSaveLocationInput.Text = s.LogFileSaveLocation;
            WeeklySummaryToggle.IsChecked       = s.WeeklySummary;
            WeeklySummaryDayCombo.SelectedIndex = s.WeeklySummaryDayIndex;
            TimestampsInLogToggle.IsChecked     = s.TimestampsInLog;
            DisconnectKickLoggingToggle.IsChecked = s.DisconnectKickLogging;

            // Performance
            ScreenCaptureFpsLimitSlider.Value   = s.ScreenCaptureFpsLimit;
            YoloThreadPriorityCombo.SelectedIndex = s.YoloThreadPriorityIndex;
            CpuThrottleThresholdSlider.Value    = s.CpuThrottleThreshold;
            ThrottleAmountSlider.Value          = s.ThrottleAmount;
            WatchdogAutoRestartToggle.IsChecked = s.WatchdogAutoRestart;
            Tf2WindowLockToggle.IsChecked       = s.Tf2WindowLock;

            // Startup / shutdown
            AutoSkipTf2IntroToggle.IsChecked         = s.AutoSkipTf2Intro;
            AutoSelectMedicToggle.IsChecked           = s.AutoSelectMedic;
            AutoEquipLoadoutOnStartToggle.IsChecked   = s.AutoEquipLoadout;
            CleanShutdownBehaviorCombo.SelectedIndex  = s.CleanShutdownIndex;
            WatchdogRestartDelaySlider.Value          = s.WatchdogRestartDelay;

            // Updates
            AutoCheckUpdatesOnLaunchToggle.IsChecked = s.AutoCheckUpdatesOnLaunch;

            // ── Bot Brain (NEW) ──────────────────────────────────────────────
            RetreatHealthThresholdSlider.Value    = s.RetreatHealthThreshold;
            DefendEnemyDistanceSlider.Value       = s.DefendEnemyDistance;
            UberPopThresholdSlider.Value          = s.UberPopThreshold;
            PreferMeleeForRetreatToggle.IsChecked = s.PreferMeleeForRetreat;
            AutoHealToggle.IsChecked              = s.AutoHealBrain;
            AutoUberBrainToggle.IsChecked         = s.AutoUberBrain;
            PriorityOnlyHealToggle.IsChecked      = s.PriorityOnlyHeal;

            // Priority list
            Priorities.Clear();
            foreach (var p in s.Priorities)
                Priorities.Add(new PriorityPlayer
                {
                    Name = p.Name, Tier = p.Tier,
                    FollowDistanceOverride = p.FollowDistanceOverride,
                    PassiveModeOverride = p.PassiveModeOverride
                });

            WhitelistEntries.Clear();
            foreach (var e in s.Whitelist) WhitelistEntries.Add(e);
            BlacklistEntries.Clear();
            foreach (var e in s.Blacklist) BlacklistEntries.Add(e);

            if (s.LoadoutPresets.Count > 0)
            {
                LoadoutPresets.Clear();
                foreach (var p in s.LoadoutPresets)
                    LoadoutPresets.Add(new LoadoutPreset
                    {
                        Name = p.Name, PrimaryWeapon = p.PrimaryWeapon,
                        SecondaryWeapon = p.SecondaryWeapon, MeleeWeapon = p.MeleeWeapon
                    });
                int idx = Math.Clamp(s.ActivePresetIndex, 0, LoadoutPresets.Count - 1);
                ActivePresetSelector.SelectedIndex = idx;
            }

            if (s.AudioCues.Count > 0)
            {
                var map = s.AudioCues.ToDictionary(a => a.Key);
                foreach (var cue in AudioCueSettings)
                    if (map.TryGetValue(cue.Key, out var saved))
                    { cue.Enabled = saved.Enabled; cue.Volume = saved.Volume; cue.CustomFilePath = saved.CustomFilePath; }
            }

            if (s.VoiceLines.Count > 0)
            {
                var map = s.VoiceLines.ToDictionary(v => v.Key);
                foreach (var vl in VoiceLineSettings)
                    if (map.TryGetValue(vl.Key, out var saved))
                    { vl.Enabled = saved.Enabled; vl.Volume = saved.Volume; vl.CustomFilePath = saved.CustomFilePath;
                      vl.TtsFallbackEnabled = saved.TtsFallback; vl.TtsVolume = saved.TtsVolume; }
            }
        }

        // ── default data init ────────────────────────────────────────────────

        private void InitializeDefaultData()
        {
            LoadoutPresets.Add(new LoadoutPreset { Name = "Default Medigun",   PrimaryWeapon = "Crusader's Crossbow", SecondaryWeapon = "Medi Gun",   MeleeWeapon = "Ubersaw" });
            LoadoutPresets.Add(new LoadoutPreset { Name = "Kritz Push",        PrimaryWeapon = "Crusader's Crossbow", SecondaryWeapon = "Kritzkrieg", MeleeWeapon = "Ubersaw" });
            LoadoutPresets.Add(new LoadoutPreset { Name = "Quick-Fix Rescue",  PrimaryWeapon = "Crusader's Crossbow", SecondaryWeapon = "Quick-Fix",  MeleeWeapon = "Solemn Vow" });
            AutoLoadoutRules.Add(new AutoLoadoutRule { TeammateClass = "Soldier", Preset = LoadoutPresets.FirstOrDefault() });

            AddAudioCue("target_switched",          "Target switched");
            AddAudioCue("uber_ready",               "Uber ready");
            AddAudioCue("uber_activated",           "Uber activated");
            AddAudioCue("uber_cooldown_started",    "Uber cooldown started");
            AddAudioCue("spy_detected",             "Spy detected");
            AddAudioCue("backstabbed",              "Backstabbed");
            AddAudioCue("lost_priority_player",     "Lost priority player");
            AddAudioCue("resupplied",               "Resupplied");
            AddAudioCue("rejoined_after_kick",      "Rejoined after kick");
            AddAudioCue("returning_to_spawn",       "Returning to spawn");
            AddAudioCue("actively_searching",       "Actively searching");
            AddAudioCue("priority_player_died",     "Priority player died");
            AddAudioCue("whitelisted_player_joined","Whitelisted player joined");
            AddAudioCue("threat_flagged",           "Threat flagged");
            AddAudioCue("tier_1_event_tone",        "Tier 1 event tone");
            AddAudioCue("tier_2_event_tone",        "Tier 2 event tone");

            AddVoiceLine("spy_detected",       "SPY detected");
            AddVoiceLine("backstabbed",        "Backstabbed");
            AddVoiceLine("uber_ready",         "Uber ready");
            AddVoiceLine("uber_activated",     "Uber activated");
            AddVoiceLine("target_died",        "Target died");
            AddVoiceLine("low_health",         "Low health");
            AddVoiceLine("uber_saved_someone", "Uber saved someone");
            AddVoiceLine("thanks_response",    "Thanks response");
            AddVoiceLine("melee_kill",         "Melee kill");

            string defaultLogDirectory = Path.Combine(_repositoryRoot ?? Directory.GetCurrentDirectory(), "logs");
            Directory.CreateDirectory(defaultLogDirectory);
            LogFileSaveLocationInput.Text = defaultLogDirectory;

            if (LoadoutPresets.Count > 0)
                ActivePresetSelector.SelectedIndex = 0;
        }

        private void AddAudioCue(string key, string label)
            => AudioCueSettings.Add(new AudioCueSetting { Key = key, Label = label });

        private void AddVoiceLine(string key, string label)
            => VoiceLineSettings.Add(new VoiceLineSetting { Key = key, Label = label });

        // ── operator state ───────────────────────────────────────────────────

        private void LoadOperatorState()
        {
            try
            {
                string path = GetOperatorStatePath();
                if (!File.Exists(path)) { _allTimeMeleeKills = 0; return; }
                dynamic? state = JsonConvert.DeserializeObject(File.ReadAllText(path));
                _allTimeMeleeKills = state?.all_time_melee_kills != null ? (int)state.all_time_melee_kills : 0;
            }
            catch { _allTimeMeleeKills = 0; }
        }

        private void SaveOperatorState()
        {
            try
            {
                string path = GetOperatorStatePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonConvert.SerializeObject(
                    new { all_time_melee_kills = _allTimeMeleeKills }, Formatting.Indented));
            }
            catch { }
        }

        private string GetOperatorStatePath()
            => Path.Combine(_repositoryRoot ?? Directory.GetCurrentDirectory(), "logs", "operator_state.json");

        private void UpdateAllTimeMeleeCounter()
        {
            if (AllTimeMeleeKillsText != null)
                AllTimeMeleeKillsText.Text = $"All-time melee kills: {_allTimeMeleeKills}";
        }

        // ── timers ───────────────────────────────────────────────────────────

        private void SetupTimers()
        {
            _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _sessionTimer.Tick += (_, _) => { _sessionSeconds++; UpdateSessionTimerText(); };

            _reconnectTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _reconnectTimer.Tick += async (_, _) =>
            {
                if (!_isConnected && AutoReconnectCB.IsChecked == true)
                    await ConnectBot();
            };
            _reconnectTimer.Start();
        }

        private void ResetSessionState()
        {
            _meleeKills = 0; _totalHealing = 0; _sessionSeconds = 0; _sessionLogExported = false;
            FollowModeLabel.Text = "Mode: STANDBY";
            UberMeterLabel.Text  = "Uber: 0%";
            UpdateSessionStats();
            UpdateSessionTimerText();
            UpdateActionButtons();
        }

        private void UpdateSessionTimerText()
        {
            int h = _sessionSeconds / 3600, m = (_sessionSeconds % 3600) / 60, s = _sessionSeconds % 60;
            SessionTimer.Text = $"{h:D2}:{m:D2}:{s:D2}";
        }

        // ── connection state ─────────────────────────────────────────────────

        private void ApplyConnectionState(bool connected, string statusText, Brush statusBrush)
        {
            _isConnected = connected;
            StatusLabel.Text = statusText; StatusLabel.Foreground = statusBrush;
            if (StatusDot != null) StatusDot.Fill = statusBrush;
            if (StatusStripDot != null) StatusStripDot.Fill = statusBrush;
            ConnectBtn.Content = connected ? "Disconnect" : "Connect";
            if (!connected) { _isBotRunning = false; FollowModeLabel.Text = "Mode: STANDBY"; _sessionTimer?.Stop(); }
            UpdateActionButtons();
        }

        private void UpdateActionButtons()
        {
            StartBtn.IsEnabled      = _isConnected && !_isBotRunning;
            StopBtn.IsEnabled       = _isConnected && _isBotRunning;
            SendConfigBtn.IsEnabled = _isConnected;
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private string GetSelectedComboContent(ComboBox combo, string fallback)
        {
            if (combo.SelectedItem is ComboBoxItem item && item.Content != null)
                return item.Content.ToString() ?? fallback;
            if (combo.SelectedItem != null)
                return combo.SelectedItem.ToString() ?? fallback;
            return string.IsNullOrWhiteSpace(combo.Text) ? fallback : combo.Text.Trim();
        }

        private LoadoutPreset GetActivePreset()
            => ActivePresetSelector.SelectedItem as LoadoutPreset
               ?? LoadoutPresets.FirstOrDefault()
               ?? new LoadoutPreset { Name = "Default Medigun", PrimaryWeapon = "Crusader's Crossbow", SecondaryWeapon = "Medi Gun", MeleeWeapon = "Ubersaw" };

        private string GetDefaultModeOnStartup()
            => GetSelectedComboContent(DefaultModeOnStartupCombo, "Active");

        private string GetUberBehaviorMode()
        {
            if (ManualOnlyToggle.IsChecked == true) return "Manual";
            if (AutoPopToggle.IsChecked    == true) return "Auto-pop";
            if (UberSuggestionAudioCueToggle.IsChecked == true) return "Suggest";
            return "Manual";
        }

        private static int ParseIntOrDefault(string? text, int fallback = 0)
            => int.TryParse(text, out int v) ? v : fallback;

        // ── config payload ───────────────────────────────────────────────────

        private object BuildBotConfigPayload()
        {
            LoadoutPreset activePreset = GetActivePreset();
            return new
            {
                connection = new
                {
                    laptop_ip_address    = string.IsNullOrWhiteSpace(BotIpInput.Text) ? "127.0.0.1" : BotIpInput.Text.Trim(),
                    port_number          = string.IsNullOrWhiteSpace(PortInput.Text) ? "8765" : PortInput.Text.Trim(),
                    auto_reconnect       = AutoReconnectCB.IsChecked == true,
                    connection_timeout_duration = (int)ConnectionTimeoutSlider.Value
                },
                primary_weapon   = activePreset.PrimaryWeapon,
                secondary_weapon = activePreset.SecondaryWeapon,
                melee_weapon     = activePreset.MeleeWeapon,
                active_preset    = activePreset.Name,
                loadout_presets  = LoadoutPresets.Select(p => new { name = p.Name, primary_weapon = p.PrimaryWeapon, secondary_weapon = p.SecondaryWeapon, melee_weapon = p.MeleeWeapon }).ToList(),
                auto_loadout_rules = AutoLoadoutRules.Select(r => new { teammate_class = r.TeammateClass, equip_preset = r.Preset?.Name ?? "" }).ToList(),
                follow_distance  = (int)FollowDistanceSlider.Value,
                follow_behavior  = new
                {
                    default_follow_distance        = (int)FollowDistanceSlider.Value,
                    active_mode_follow_distance    = (int)ActiveModeFollowDistanceSlider.Value,
                    passive_mode_follow_distance   = (int)PassiveModeFollowDistanceSlider.Value,
                    escort_mode_follow_distance    = (int)EscortModeFollowDistanceSlider.Value,
                    erratic_movement_threshold     = (int)ErraticMovementThresholdSlider.Value,
                    search_timeout                 = (int)SearchModeTimeoutSlider.Value,
                    stuck_detection_sensitivity    = (int)StuckDetectionSensitivitySlider.Value,
                    stuck_detection_retry_attempts = (int)StuckDetectionRetrySlider.Value,
                    ledge_correction_sensitivity   = (int)LedgeCorrectionSensitivitySlider.Value
                },
                uber_behavior = GetUberBehaviorMode(),
                uber = new
                {
                    auto_pop                    = AutoPopToggle.IsChecked == true,
                    auto_pop_health_threshold   = (int)AutoPopHealthThresholdSlider.Value,
                    manual_only                 = ManualOnlyToggle.IsChecked == true,
                    uber_suggestion_audio_cue   = UberSuggestionAudioCueToggle.IsChecked == true,
                    hold_uber_if_priority_is_dead = HoldUberIfPriorityDeadToggle.IsChecked == true,
                    defensive_uber              = DefensiveUberToggle.IsChecked == true
                },
                spy_check_frequency = (int)SpyCheckFreqSlider.Value,
                spy_detection = new
                {
                    spy_check_frequency                              = (int)SpyCheckFreqSlider.Value,
                    spy_alertness_duration_after_backstab            = (int)SpyAlertnessDurationSlider.Value,
                    spy_alertness_increase_after_kill_feed_spy_death = SpyAlertnessIncreaseAfterKillFeedToggle.IsChecked == true,
                    spy_check_camera_flick_speed                     = (int)SpyCheckCameraFlickSpeedSlider.Value
                },
                scanning = new
                {
                    idle_rotation_speed                  = (int)IdleRotationSpeedSlider.Value,
                    rotation_speed_when_callout_detected = (int)CalloutRotationSpeedSlider.Value,
                    rotation_degrees_per_step            = (int)RotationDegreesPerStepSlider.Value,
                    yolo_confidence_threshold            = (int)YoloConfidenceThresholdSlider.Value,
                    ocr_sensitivity                      = (int)OcrSensitivitySlider.Value,
                    search_mode_rotation_speed           = (int)SearchModeRotationSpeedSlider.Value
                },
                melee_mode_enabled = MeleeModeToggle.IsChecked == true,
                melee_mode = new
                {
                    enabled                         = MeleeModeToggle.IsChecked == true,
                    outnumbered_retreat_threshold   = (int)OutnumberedRetreatThresholdSlider.Value,
                    critical_health_retreat_threshold = (int)CriticalHealthRetreatThresholdSlider.Value,
                    melee_kill_voice_line           = MeleeKillVoiceLineToggle.IsChecked == true,
                    melee_kill_logging              = MeleeKillLoggingToggle.IsChecked == true
                },
                passive_mode = new
                {
                    default_mode_on_startup = GetDefaultModeOnStartup(),
                    double_callout_timeout  = Math.Round(DoubleCalloutTimeoutSlider.Value, 1),
                    idle_rotation_speed     = (int)PassiveModeIdleRotationSpeedSlider.Value,
                    enemy_avoidance         = PassiveModeEnemyAvoidanceToggle.IsChecked == true,
                    idle_audio_cue          = PassiveIdleAudioCueToggle.IsChecked == true,
                    idle_audio_cue_interval = (int)PassiveIdleAudioCueIntervalSlider.Value
                },
                master_volume             = (int)MasterVolumeSlider.Value,
                audio_cues                = AudioCueSettings.Select(a => new { key = a.Key, label = a.Label, enabled = a.Enabled, volume = a.Volume, custom_file = a.CustomFilePath }).ToList(),
                master_voice_line_enabled = MasterVoiceLineToggle.IsChecked == true,
                voice_lines               = VoiceLineSettings.Select(v => new { key = v.Key, label = v.Label, enabled = v.Enabled, volume = v.Volume, custom_file = v.CustomFilePath, tts_fallback = v.TtsFallbackEnabled, tts_volume = v.TtsVolume }).ToList(),
                scoreboard_checks = new
                {
                    scoreboard_check_frequency                = (int)ScoreboardCheckFrequencySlider.Value,
                    tab_hold_duration                         = Math.Round(TabHoldDurationSlider.Value, 2),
                    auto_update_whitelist_from_scoreboard     = AutoUpdateWhitelistFromScoreboardToggle.IsChecked == true,
                    auto_whitelist_detection_from_scoreboard  = AutoWhitelistDetectionCheckBox.IsChecked == true,
                    class_detection                           = ClassDetectionToggle.IsChecked == true
                },
                session_logging = new
                {
                    auto_export_log_after_session = AutoExportLogAfterSessionToggle.IsChecked == true,
                    log_file_save_location        = LogFileSaveLocationInput.Text.Trim(),
                    weekly_summary                = WeeklySummaryToggle.IsChecked == true,
                    weekly_summary_day            = GetSelectedComboContent(WeeklySummaryDayCombo, "Monday"),
                    timestamps_in_log             = TimestampsInLogToggle.IsChecked == true,
                    disconnect_and_kick_logging   = DisconnectKickLoggingToggle.IsChecked == true,
                    melee_kill_all_time_counter   = _allTimeMeleeKills
                },
                performance = new
                {
                    screen_capture_fps_limit           = (int)ScreenCaptureFpsLimitSlider.Value,
                    yolo_thread_priority               = GetSelectedComboContent(YoloThreadPriorityCombo, "Normal"),
                    cpu_temperature_throttle_threshold = (int)CpuThrottleThresholdSlider.Value,
                    throttle_amount_when_overheating   = (int)ThrottleAmountSlider.Value,
                    watchdog_auto_restart              = WatchdogAutoRestartToggle.IsChecked == true,
                    tf2_window_lock                    = Tf2WindowLockToggle.IsChecked == true
                },
                startup_shutdown = new
                {
                    auto_skip_tf2_intro         = AutoSkipTf2IntroToggle.IsChecked == true,
                    auto_select_medic           = AutoSelectMedicToggle.IsChecked == true,
                    auto_equip_loadout_on_start = AutoEquipLoadoutOnStartToggle.IsChecked == true,
                    clean_shutdown_behavior     = GetSelectedComboContent(CleanShutdownBehaviorCombo, "Idle in spawn"),
                    watchdog_restart_delay      = (int)WatchdogRestartDelaySlider.Value
                },
                updates = new { auto_check_on_launch = AutoCheckUpdatesOnLaunchToggle.IsChecked == true },

                // ── Bot Brain section (NEW) ───────────────────────────────────
                brain_config = new
                {
                    retreat_health_threshold = (int)RetreatHealthThresholdSlider.Value,
                    defend_enemy_distance    = (int)DefendEnemyDistanceSlider.Value,
                    uber_pop_threshold       = (int)UberPopThresholdSlider.Value,
                    prefer_melee_for_retreat = PreferMeleeForRetreatToggle.IsChecked == true,
                    auto_heal                = AutoHealToggle.IsChecked == true,
                    auto_uber                = AutoUberBrainToggle.IsChecked == true,
                    priority_only_heal       = PriorityOnlyHealToggle.IsChecked == true,
                    priority_players         = Priorities.Select(p => p.Name).ToList()
                },

                priority_list = Priorities.Select(p => new { name = p.Name, tier = p.Tier, follow_distance_override = ParseIntOrDefault(p.FollowDistanceOverride), passive_mode_override = p.PassiveModeOverride }).ToList(),
                whitelist     = WhitelistEntries.ToList(),
                blacklist     = BlacklistEntries.ToList()
            };
        }

        // ══════════════════════════════════════════════════════════════════════
        // Bot Brain HTTP sync  (NEW)
        // Sends brain-specific thresholds to the Flask server on port 5000.
        // This is separate from the WebSocket config path.
        // ══════════════════════════════════════════════════════════════════════

        private string GetBotHttpBase()
        {
            string ip = string.IsNullOrWhiteSpace(BotIpInput.Text) ? "127.0.0.1" : BotIpInput.Text.Trim();
            return $"http://{ip}:5000";
        }

        private async Task SyncBrainConfigAsync()
        {
            var payload = new
            {
                retreat_health_threshold = (int)RetreatHealthThresholdSlider.Value,
                defend_enemy_distance    = (int)DefendEnemyDistanceSlider.Value,
                uber_pop_threshold       = (int)UberPopThresholdSlider.Value,
                prefer_melee_for_retreat = PreferMeleeForRetreatToggle.IsChecked == true,
                auto_heal                = AutoHealToggle.IsChecked == true,
                auto_uber                = AutoUberBrainToggle.IsChecked == true,
                priority_only_heal       = PriorityOnlyHealToggle.IsChecked == true,
                priority_players         = Priorities.Select(p => p.Name).ToList()
            };

            string json    = JsonConvert.SerializeObject(payload);
            var    content = new StringContent(json, Encoding.UTF8, "application/json");
            string url     = $"{GetBotHttpBase()}/config";

            var response = await _http.PostAsync(url, content);
            if (response.IsSuccessStatusCode)
                AppendActivity("[SYNC] Brain config pushed to bot (HTTP).");
            else
                AppendActivity($"[ERR] Brain config sync failed: HTTP {(int)response.StatusCode}", true);
        }

        private async void SyncBrainConfigBtn_Click(object sender, RoutedEventArgs e)
        {
            try { await SyncBrainConfigAsync(); }
            catch (Exception ex) { AppendActivity($"[ERR] Brain sync error: {ex.Message}", true); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // Event handlers  (connection, bot control, lists, UI)
        // ══════════════════════════════════════════════════════════════════════

        private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected) await DisconnectBot();
            else await ConnectBot();
        }

        private Uri BuildBotUri()
        {
            string ip   = string.IsNullOrWhiteSpace(BotIpInput.Text)  ? "127.0.0.1" : BotIpInput.Text.Trim();
            string port = string.IsNullOrWhiteSpace(PortInput.Text)   ? "8765"       : PortInput.Text.Trim();
            return new Uri($"ws://{ip}:{port}");
        }

        private async void TestConnectionBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var probe   = new ClientWebSocket();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds((int)ConnectionTimeoutSlider.Value));
                Uri endpoint = BuildBotUri();
                await probe.ConnectAsync(endpoint, timeout.Token);
                if (probe.State == WebSocketState.Open)
                    await probe.CloseAsync(WebSocketCloseStatus.NormalClosure, "Probe complete", CancellationToken.None);
                AppendActivity($"[OK] Test connection succeeded: {endpoint.Host}:{endpoint.Port}");
            }
            catch (Exception ex) { AppendActivity($"[ERR] Test connection failed: {ex.Message}", true); }
        }

        private async Task ConnectBot()
        {
            if (_isConnected) return;
            ApplyConnectionState(false, "Linking...", Brushes.Goldenrod);
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _ws?.Dispose();
            _ws = new ClientWebSocket();
            Uri endpoint = BuildBotUri();
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds((int)ConnectionTimeoutSlider.Value));
                await _ws.ConnectAsync(endpoint, timeoutCts.Token);
                ApplyConnectionState(true, "Online", Brushes.LightGreen);
                await SendJson(new { type = "config", config = BuildBotConfigPayload() });
                AppendActivity($"[OK] Linked to bot at {endpoint.Host}:{endpoint.Port}");
                AppendActivity("[SYNC] Active profile pushed on connect.");
                _ = ListenLoop();
            }
            catch (OperationCanceledException) when (_cts?.IsCancellationRequested == false)
            {
                _ws?.Dispose(); _ws = null;
                ApplyConnectionState(false, "Offline", Brushes.IndianRed);
                AppendActivity("[ERR] Connection attempt timed out.", true);
            }
            catch (Exception ex)
            {
                _ws?.Dispose(); _ws = null;
                ApplyConnectionState(false, "Offline", Brushes.IndianRed);
                AppendActivity($"[ERR] Connection failed: {ex.Message}", true);
            }
        }

        private async Task DisconnectBot()
        {
            try
            {
                _cts?.Cancel();
                if (_ws?.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
                _ws?.Dispose(); _ws = null;
            }
            catch { }
            finally
            {
                ApplyConnectionState(false, "Offline", Brushes.IndianRed);
                AppendActivity("[INFO] Disconnected from bot.");
                TryAutoExportSessionLog();
            }
        }

        private async Task ListenLoop()
        {
            byte[] buffer = new byte[1024 * 16];
            try
            {
                while (_ws != null && _ws.State == WebSocketState.Open && _isConnected)
                {
                    await using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        if (_cts == null) return;
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                            Dispatcher.Invoke(SetDisconnectedUi);
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    string message = Encoding.UTF8.GetString(ms.ToArray());
                    try
                    {
                        dynamic? data = JsonConvert.DeserializeObject(message);
                        if (data != null) Dispatcher.Invoke(() => HandleBotMessage(data));
                    }
                    catch { }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => { SetDisconnectedUi(); AppendActivity($"[ERR] Bot link dropped: {ex.Message}", true); });
            }
        }

        private void HandleBotMessage(dynamic data)
        {
            string type = data.type?.ToString() ?? string.Empty;
            if (type == "activity")
            {
                string message = data.msg?.ToString() ?? string.Empty;
                bool isMeleeKill = message.Contains("melee kill", StringComparison.OrdinalIgnoreCase);
                bool isError = message.Contains("error", StringComparison.OrdinalIgnoreCase)
                            || message.Contains("failed", StringComparison.OrdinalIgnoreCase);
                if (isMeleeKill)
                {
                    _meleeKills++; _allTimeMeleeKills++;
                    UpdateSessionStats(); UpdateAllTimeMeleeCounter(); SaveOperatorState();
                    if (MeleeKillLoggingToggle.IsChecked != true) return;
                }
                AppendActivity(message, isError);
                return;
            }
            if (type == "status")
            {
                try
                {
                    // Safe dynamic access using reflection-like approach
                    var dict = (IDictionary<string, object>)data;
                    if (dict.ContainsKey("uber"))
                        UberMeterLabel.Text = $"Uber: {data.uber}%";
                }
                catch { }
                try
                {
                    var dict = (IDictionary<string, object>)data;
                    if (dict.ContainsKey("mode"))
                        FollowModeLabel.Text = $"Mode: {data.mode?.ToString().ToUpperInvariant()}";
                }
                catch { }
            }
        }

        private void SetDisconnectedUi() => ApplyConnectionState(false, "Offline", Brushes.IndianRed);

        private async Task SendJson(object payload)
        {
            if (_ws?.State != WebSocketState.Open || _cts == null) return;
            byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
            await _ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, _cts.Token);
        }

        private async void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected) { AppendActivity("[ERR] Connect to the bot before deploying.", true); return; }
            try
            {
                await SendJson(new { type = "config", config = BuildBotConfigPayload() });
                await SendJson(new { type = "start" });
                _isBotRunning = true; _meleeKills = 0; _totalHealing = 0;
                _sessionSeconds = 0; _sessionLogExported = false;
                _sessionTimer?.Start();
                UpdateSessionStats();
                FollowModeLabel.Text = string.Equals(GetDefaultModeOnStartup(), "Passive", StringComparison.OrdinalIgnoreCase)
                    ? "Mode: PASSIVE" : "Mode: ACTIVE";
                UpdateActionButtons();
                AppendActivity("[OK] Medic bot deployed.");
            }
            catch (Exception ex) { AppendActivity($"[ERR] Deploy failed: {ex.Message}", true); }
        }

        private async void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected) { AppendActivity("[ERR] Bot link is offline.", true); return; }
            try
            {
                await SendJson(new { type = "stop" });
                _isBotRunning = false; _sessionTimer?.Stop();
                FollowModeLabel.Text = "Mode: STANDBY";
                UpdateActionButtons();
                AppendActivity("[INFO] Stop command sent to bot.");
                TryAutoExportSessionLog();
            }
            catch (Exception ex) { AppendActivity($"[ERR] Stop failed: {ex.Message}", true); }
        }

        private async void SendConfigBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected) { AppendActivity("[ERR] Connect to the bot before syncing config.", true); return; }
            try { await SendJson(new { type = "config", config = BuildBotConfigPayload() }); AppendActivity("[SYNC] Configuration pushed to bot."); }
            catch (Exception ex) { AppendActivity($"[ERR] Config sync failed: {ex.Message}", true); }
        }

        // ── priority list ─────────────────────────────────────────────────────

        private void AddPriority_Click(object sender, RoutedEventArgs e)
        {
            string name = PriorityNameInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            if (Priorities.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            { AppendActivity($"[WARN] {name} is already marked as priority.", true); return; }
            int tier = TierInput.SelectedItem is int t ? t : 1;
            Priorities.Add(new PriorityPlayer { Name = name, Tier = Math.Clamp(tier, 1, 3) });
            PriorityNameInput.Clear(); TierInput.SelectedIndex = 0;
            AppendActivity($"[OK] Added priority target: {name} (Tier {tier}).");
        }

        private void ApplyTierSlots_Click(object sender, RoutedEventArgs e)
        {
            ApplyTierSlot(Tier1PriorityInput.Text, 1);
            ApplyTierSlot(Tier2PriorityInput.Text, 2);
            ApplyTierSlot(Tier3PriorityInput.Text, 3);
            AppendActivity("[OK] Tier slots applied.");
        }

        private void ApplyTierSlot(string rawName, int tier)
        {
            string name = rawName.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            var existing = Priorities.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { existing.Tier = tier; return; }
            Priorities.Add(new PriorityPlayer { Name = name, Tier = tier });
        }

        private void RemovePriority_Click(object sender, RoutedEventArgs e)
        {
            if (PriorityListView.SelectedItem is PriorityPlayer p) { Priorities.Remove(p); AppendActivity($"[INFO] Removed priority target: {p.Name}."); }
            else AppendActivity("[ERR] Select a priority target first.", true);
        }

        private void MovePriorityUp_Click(object sender, RoutedEventArgs e)
        {
            if (PriorityListView.SelectedItem is not PriorityPlayer p) { AppendActivity("[ERR] Select a priority target first.", true); return; }
            int i = Priorities.IndexOf(p); if (i <= 0) return;
            Priorities.Move(i, i - 1); PriorityListView.SelectedItem = p;
        }

        private void MovePriorityDown_Click(object sender, RoutedEventArgs e)
        {
            if (PriorityListView.SelectedItem is not PriorityPlayer p) { AppendActivity("[ERR] Select a priority target first.", true); return; }
            int i = Priorities.IndexOf(p); if (i < 0 || i >= Priorities.Count - 1) return;
            Priorities.Move(i, i + 1); PriorityListView.SelectedItem = p;
        }

        // ── loadout rules ─────────────────────────────────────────────────────

        private void AddLoadoutRule_Click(object sender, RoutedEventArgs e)
            => AutoLoadoutRules.Add(new AutoLoadoutRule { TeammateClass = TeammateClassOptions.First(), Preset = GetActivePreset() });

        private void RemoveLoadoutRule_Click(object sender, RoutedEventArgs e)
        {
            if (AutoLoadoutRuleListView.SelectedItem is AutoLoadoutRule r) AutoLoadoutRules.Remove(r);
            else AppendActivity("[ERR] Select a loadout rule first.", true);
        }

        // ── whitelist / blacklist ─────────────────────────────────────────────

        private void AddWhitelist_Click(object sender, RoutedEventArgs e)
        {
            string name = WhitelistInput.Text.Trim(); if (string.IsNullOrWhiteSpace(name)) return;
            if (WhitelistEntries.Any(e2 => e2.Equals(name, StringComparison.OrdinalIgnoreCase)))
            { AppendActivity($"[WARN] {name} is already whitelisted.", true); return; }
            WhitelistEntries.Add(name); WhitelistInput.Clear(); AppendActivity($"[OK] Whitelisted {name}.");
        }

        private void RemoveWhitelist_Click(object sender, RoutedEventArgs e)
        { if (WhitelistBox.SelectedItem is string name) { WhitelistEntries.Remove(name); AppendActivity($"[INFO] Removed {name} from whitelist."); } }

        private void AddBlacklist_Click(object sender, RoutedEventArgs e)
        {
            string name = BlacklistInput.Text.Trim(); if (string.IsNullOrWhiteSpace(name)) return;
            if (BlacklistEntries.Any(e2 => e2.Equals(name, StringComparison.OrdinalIgnoreCase)))
            { AppendActivity($"[WARN] {name} is already blacklisted.", true); return; }
            BlacklistEntries.Add(name); BlacklistInput.Clear(); AppendActivity($"[OK] Blacklisted {name}.");
        }

        private void RemoveBlacklist_Click(object sender, RoutedEventArgs e)
        { if (BlacklistBox.SelectedItem is string name) { BlacklistEntries.Remove(name); AppendActivity($"[INFO] Removed {name} from blacklist."); } }

        private void QuickBanBtn_Click(object sender, RoutedEventArgs e)
        {
            string name = BlacklistInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) { AppendActivity("[ERR] Enter a name in the blacklist field first.", true); return; }
            if (!BlacklistEntries.Any(e2 => e2.Equals(name, StringComparison.OrdinalIgnoreCase))) BlacklistEntries.Add(name);
            BlacklistInput.Clear(); AppendActivity($"[WARN] Quick banned {name} for the current match.");
        }

        // ── stats / logging ───────────────────────────────────────────────────

        private void ResetStatsBtn_Click(object sender, RoutedEventArgs e) { ResetSessionState(); AppendActivity("[INFO] Session stats reset."); }

        private void UpdateSessionStats()
        {
            if (MeleeKillsStatLabel != null) MeleeKillsStatLabel.Text = $"Melee picks: {_meleeKills}";
            if (TotalHealingStatLabel != null) TotalHealingStatLabel.Text = $"Healing output: {_totalHealing:N0} HP";
            UpdateAllTimeMeleeCounter();
        }

        private void OpenSoundFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            string folder = Path.Combine(_repositoryRoot ?? Directory.GetCurrentDirectory(), "assets", "sounds");
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{folder}\"", UseShellExecute = true });
            AppendActivity($"[OK] Opened sound folder: {folder}");
        }

        private void UseDefaultLogLocationBtn_Click(object sender, RoutedEventArgs e)
        {
            string dir = Path.Combine(_repositoryRoot ?? Directory.GetCurrentDirectory(), "logs");
            Directory.CreateDirectory(dir);
            LogFileSaveLocationInput.Text = dir;
            AppendActivity($"[OK] Log location reset to {dir}");
        }

        private void ExportActivityLogBtn_Click(object sender, RoutedEventArgs e) => ExportActivityLog(showMessage: true);

        private void TryAutoExportSessionLog()
        {
            if (_sessionLogExported || AutoExportLogAfterSessionToggle.IsChecked != true || _sessionSeconds <= 0) return;
            ExportActivityLog(showMessage: true); _sessionLogExported = true;
        }

        private void ExportActivityLog(bool showMessage)
        {
            try
            {
                string dir = string.IsNullOrWhiteSpace(LogFileSaveLocationInput.Text)
                    ? Path.Combine(_repositoryRoot ?? Directory.GetCurrentDirectory(), "logs")
                    : LogFileSaveLocationInput.Text.Trim();
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"medicai_session_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                var range = new TextRange(ActivityLog.Document.ContentStart, ActivityLog.Document.ContentEnd);
                File.WriteAllText(path, range.Text.Trim());
                if (showMessage) AppendActivity($"[OK] Log exported to {path}");
            }
            catch (Exception ex) { AppendActivity($"[ERR] Log export failed: {ex.Message}", true); }
        }

        private void UploadAudioFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Audio Files|*.wav;*.mp3;*.wma;*.aac;*.m4a|All Files|*.*", CheckFileExists = true };
            string soundDir = Path.Combine(_repositoryRoot ?? Directory.GetCurrentDirectory(), "assets", "sounds");
            if (Directory.Exists(soundDir)) dialog.InitialDirectory = soundDir;
            if (dialog.ShowDialog(this) != true) return;
            switch ((sender as FrameworkElement)?.DataContext)
            {
                case AudioCueSetting a: a.CustomFilePath = dialog.FileName; AppendActivity($"[OK] Attached custom audio cue for {a.Label}."); break;
                case VoiceLineSetting v: v.CustomFilePath = dialog.FileName; AppendActivity($"[OK] Attached custom voice line for {v.Label}."); break;
            }
        }

        private void PreviewVoiceLine_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not VoiceLineSetting vl) return;
            try
            {
                _previewPlayer.Stop();
                _previewPlayer.Close();
                if (!string.IsNullOrWhiteSpace(vl.CustomFilePath) && File.Exists(vl.CustomFilePath))
                {
                    _previewPlayer.Open(new Uri(vl.CustomFilePath, UriKind.Absolute));
                    _previewPlayer.Volume = vl.Volume / 100d;
                    _previewPlayer.Play();
                    AppendActivity($"[OK] Previewing voice line: {vl.Label}");
                    return;
                }
                SystemSounds.Asterisk.Play();
                AppendActivity($"[INFO] No custom file assigned for {vl.Label}; played fallback preview.");
            }
            catch (Exception ex) { AppendActivity($"[ERR] Voice line preview failed: {ex.Message}", true); }
        }

        // ── update tab ────────────────────────────────────────────────────────

        private async void CheckUpdatesBtn_Click(object sender, RoutedEventArgs e)
            => await RunUpdateOperationAsync(async () => { AppendActivity("[UPD] Checking remote for updates..."); await RefreshUpdateStatusAsync(fetchRemote: true, logErrors: true); });

        private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            await RunUpdateOperationAsync(async () =>
            {
                var snap = await GetUpdateSnapshotAsync(fetchRemote: true, CancellationToken.None);
                ApplyUpdateSnapshot(snap);
                if (!snap.IsRepositoryAvailable) { AppendActivity("[ERR] In-app updating needs a git clone.", true); return; }
                if (snap.WorktreeDirty) { AppendActivity("[ERR] Update blocked – local changes present.", true); return; }
                if (snap.BehindCount <= 0) { AppendActivity("[OK] MedicAI is already up to date."); return; }
                AppendActivity($"[UPD] Pulling {snap.BehindCount} update(s)...");
                var pull = await RunProcessAsync("git", $"pull --ff-only origin {snap.Branch}", _repositoryRoot!, CancellationToken.None, 90000);
                if (!pull.Succeeded) { AppendActivity($"[ERR] Update failed: {FirstMeaningfulLine(pull.CombinedOutput)}", true); return; }
                AppendActivity("[OK] Update installed.");
                await RefreshUpdateStatusAsync(fetchRemote: false, logErrors: false);
                if (MessageBox.Show(this, "Update pulled. Relaunch via start.bat now?", "Updated", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    RelaunchFromStartScript();
            });
        }

        private async void RollbackBtn_Click(object sender, RoutedEventArgs e)
        {
            await RunUpdateOperationAsync(async () =>
            {
                var snap = await GetUpdateSnapshotAsync(fetchRemote: false, CancellationToken.None);
                ApplyUpdateSnapshot(snap);
                if (!snap.IsRepositoryAvailable) { AppendActivity("[ERR] Rollback needs a git clone.", true); return; }
                if (snap.WorktreeDirty) { AppendActivity("[ERR] Rollback blocked – local changes present.", true); return; }
                if (MessageBox.Show(this, "Hard reset to previous commit. Continue?", "Confirm Rollback", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                var result = await RunProcessAsync("git", "reset --hard HEAD~1", _repositoryRoot!, CancellationToken.None, 90000);
                if (!result.Succeeded) { AppendActivity($"[ERR] Rollback failed: {FirstMeaningfulLine(result.CombinedOutput)}", true); return; }
                AppendActivity("[OK] Rolled back to previous commit.");
                await RefreshUpdateStatusAsync(fetchRemote: false, logErrors: false);
            });
        }

        private async Task RunUpdateOperationAsync(Func<Task> op)
        {
            if (_isUpdateOperationRunning) return;
            _isUpdateOperationRunning = true; SetUpdateUiBusy(true);
            try { await op(); }
            catch (Exception ex) { AppendActivity($"[ERR] Update workflow failed: {ex.Message}", true); }
            finally { _isUpdateOperationRunning = false; SetUpdateUiBusy(false); }
        }

        private void SetUpdateUiBusy(bool busy)
        {
            CheckUpdatesBtn.IsEnabled = !busy;
            if (busy) { UpdateBtn.IsEnabled = false; RollbackBtn.IsEnabled = false; CheckUpdatesBtn.Content = "Checking..."; UpdateBtn.Content = "Working..."; return; }
            CheckUpdatesBtn.Content = "Check Remote";
            if (UpdateBtn.Tag is string lbl && !string.IsNullOrWhiteSpace(lbl)) UpdateBtn.Content = lbl;
        }

        private async Task RefreshUpdateStatusAsync(bool fetchRemote, bool logErrors)
        {
            try { var snap = await GetUpdateSnapshotAsync(fetchRemote, CancellationToken.None); ApplyUpdateSnapshot(snap); await RefreshChangelogAsync(); }
            catch (Exception ex)
            {
                SetUpdateStatus("Update status unavailable", FirstMeaningfulLine(ex.Message), _repositoryRoot ?? "No repository detected");
                if (logErrors) AppendActivity($"[ERR] Could not read update state: {ex.Message}", true);
            }
        }

        private void ApplyUpdateSnapshot(UpdateSnapshot snap)
        {
            string ver = snap.IsRepositoryAvailable ? $"{snap.Branch} @ {snap.Commit}" : "Standalone build";
            SetUpdateStatus(snap.Summary, snap.Detail, snap.RepoPath);
            VersionValueText.Text = ver;
            string lbl = snap.BehindCount > 0 ? $"Install {snap.BehindCount} Update{(snap.BehindCount == 1 ? "" : "s")}" : "Install Update";
            UpdateBtn.Tag = lbl;
            if (!_isUpdateOperationRunning) { UpdateBtn.Content = lbl; UpdateBtn.IsEnabled = snap.IsRepositoryAvailable && !snap.WorktreeDirty && snap.BehindCount > 0; RollbackBtn.IsEnabled = snap.IsRepositoryAvailable && !snap.WorktreeDirty; }
        }

        private void SetUpdateStatus(string s, string d, string r) { UpdateStateText.Text = s; UpdateHintText.Text = d; RepoPathText.Text = r; }

        private async Task RefreshChangelogAsync()
        {
            if (string.IsNullOrWhiteSpace(_repositoryRoot)) { ChangelogViewer.Text = "Changelog unavailable outside a git clone."; return; }
            var r = await RunProcessAsync("git", "log --oneline -n 12", _repositoryRoot, CancellationToken.None);
            ChangelogViewer.Text = r.Succeeded && !string.IsNullOrWhiteSpace(r.StandardOutput) ? r.StandardOutput.Trim() : FirstMeaningfulLine(r.CombinedOutput);
        }

        private async Task<UpdateSnapshot> GetUpdateSnapshotAsync(bool fetchRemote, CancellationToken ct)
        {
            string repoPath = _repositoryRoot ?? AppContext.BaseDirectory;
            if (string.IsNullOrWhiteSpace(_repositoryRoot))
                return new UpdateSnapshot { IsRepositoryAvailable = false, Summary = "Update controls need a git clone", Detail = "Repository root not found.", RepoPath = repoPath };

            var gitVer = await RunProcessAsync("git", "--version", _repositoryRoot, ct);
            if (!gitVer.Succeeded) return new UpdateSnapshot { IsRepositoryAvailable = false, Summary = "Git is not available", Detail = FirstMeaningfulLine(gitVer.CombinedOutput), RepoPath = repoPath };

            var repoCheck = await RunProcessAsync("git", "rev-parse --is-inside-work-tree", _repositoryRoot, ct);
            if (!repoCheck.Succeeded || !repoCheck.StandardOutput.Contains("true", StringComparison.OrdinalIgnoreCase))
                return new UpdateSnapshot { IsRepositoryAvailable = false, Summary = "Not inside a git worktree", Detail = "In-app updates only work from the repository folder.", RepoPath = repoPath };

            if (fetchRemote)
            {
                var fetch = await RunProcessAsync("git", "fetch origin --prune", _repositoryRoot, ct, 90000);
                if (!fetch.Succeeded) return new UpdateSnapshot { IsRepositoryAvailable = true, Summary = "Remote check failed", Detail = FirstMeaningfulLine(fetch.CombinedOutput), RepoPath = repoPath };
            }

            var branchR   = await RunProcessAsync("git", "branch --show-current",           _repositoryRoot, ct);
            var commitR   = await RunProcessAsync("git", "rev-parse --short HEAD",           _repositoryRoot, ct);
            var statusR   = await RunProcessAsync("git", "status --porcelain",              _repositoryRoot, ct);
            var upstreamR = await RunProcessAsync("git", "rev-parse --abbrev-ref --symbolic-full-name HEAD@{upstream}", _repositoryRoot, ct);

            string branch        = string.IsNullOrWhiteSpace(branchR.StandardOutput) ? "main" : branchR.StandardOutput.Trim();
            string commit        = string.IsNullOrWhiteSpace(commitR.StandardOutput) ? "unknown" : commitR.StandardOutput.Trim();
            bool   worktreeDirty = !string.IsNullOrWhiteSpace(statusR.StandardOutput);
            string upstream      = upstreamR.Succeeded && !string.IsNullOrWhiteSpace(upstreamR.StandardOutput) ? upstreamR.StandardOutput.Trim() : $"origin/{branch}";

            var upstreamVerify = await RunProcessAsync("git", $"rev-parse --verify {upstream}", _repositoryRoot, ct);
            if (!upstreamVerify.Succeeded)
                return new UpdateSnapshot { IsRepositoryAvailable = true, Branch = branch, Commit = commit, WorktreeDirty = worktreeDirty, Summary = "Remote tracking not configured", Detail = "Set an upstream branch to enable update checks.", RepoPath = repoPath };

            var divR = await RunProcessAsync("git", $"rev-list --left-right --count HEAD...{upstream}", _repositoryRoot, ct);
            int ahead = 0, behind = 0;
            if (divR.Succeeded)
            {
                var parts = divR.StandardOutput.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) { int.TryParse(parts[0], out ahead); int.TryParse(parts[1], out behind); }
            }

            string summary = behind > 0 && worktreeDirty ? $"{behind} update(s) ready, local edits blocking install"
                           : behind > 0 ? $"{behind} update(s) available"
                           : worktreeDirty ? "No remote updates, local edits present"
                           : fetchRemote ? "Branch is synced with origin" : "Local branch looks current";
            string detail  = behind > 0 && worktreeDirty ? "Commit or stash local changes before updating."
                           : behind > 0 ? "Click Install Update to pull changes."
                           : worktreeDirty ? "Install Update stays disabled until the tree is clean."
                           : "Run Check Remote to verify against origin.";

            return new UpdateSnapshot { IsRepositoryAvailable = true, Branch = branch, Commit = commit, WorktreeDirty = worktreeDirty, AheadCount = ahead, BehindCount = behind, Summary = summary, Detail = detail, RepoPath = repoPath };
        }

        private async Task<ProcessResult> RunProcessAsync(string fileName, string arguments, string workingDir, CancellationToken ct, int timeoutMs = 30000)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);
            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName, Arguments = arguments, WorkingDirectory = workingDir,
                        RedirectStandardOutput = true, RedirectStandardError = true,
                        UseShellExecute = false, CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                    }
                };
                proc.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
                proc.Start();
                var outTask = proc.StandardOutput.ReadToEndAsync();
                var errTask = proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync(timeoutCts.Token);
                return new ProcessResult { ExitCode = proc.ExitCode, StandardOutput = await outTask, StandardError = await errTask };
            }
            catch (OperationCanceledException) { return new ProcessResult { ExitCode = -1, StandardError = $"Timed out: {fileName} {arguments}" }; }
            catch (Exception ex) { return new ProcessResult { ExitCode = -1, StandardError = ex.Message }; }
        }

        private void RelaunchFromStartScript()
        {
            if (string.IsNullOrWhiteSpace(_repositoryRoot)) { AppendActivity("[ERR] Could not locate start.bat.", true); return; }
            string script = Path.Combine(_repositoryRoot, "start.bat");
            if (!File.Exists(script)) { AppendActivity("[ERR] start.bat not found.", true); return; }
            Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c timeout /t 2 /nobreak > nul && call \"{script}\"", WorkingDirectory = _repositoryRoot, UseShellExecute = false, CreateNoWindow = true });
            Close();
        }

        private static string FirstMeaningfulLine(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "No additional details.";
            var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            return lines.Length == 0 ? "No additional details." : lines[0];
        }

        private static string? FindRepositoryRoot()
        {
            foreach (string probe in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                DirectoryInfo? cur = new(probe);
                while (cur != null)
                {
                    if (Directory.Exists(Path.Combine(cur.FullName, ".git")) || File.Exists(Path.Combine(cur.FullName, "MedicAI.sln"))) return cur.FullName;
                    cur = cur.Parent;
                }
            }
            return null;
        }

        // ── activity log ─────────────────────────────────────────────────────

        private void AppendActivity(string text, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                if (_isInitialized && DisconnectKickLoggingToggle.IsChecked != true &&
                    (text.Contains("disconnect", StringComparison.OrdinalIgnoreCase) || text.Contains("kick", StringComparison.OrdinalIgnoreCase))) return;

                string display = _isInitialized && TimestampsInLogToggle.IsChecked == true ? $"[{DateTime.Now:HH:mm:ss}] {text}" : text;

                Brush color = isError || text.StartsWith("[ERR]", StringComparison.OrdinalIgnoreCase) ? Brushes.IndianRed
                            : text.StartsWith("[OK]",   StringComparison.OrdinalIgnoreCase) ? Brushes.LightGreen
                            : text.StartsWith("[UPD]",  StringComparison.OrdinalIgnoreCase) ? Brushes.Gold
                            : text.StartsWith("[SYNC]", StringComparison.OrdinalIgnoreCase) ? Brushes.LightSkyBlue
                            : text.StartsWith("[WARN]", StringComparison.OrdinalIgnoreCase) ? Brushes.Orange
                            : Brushes.Gainsboro;

                ActivityLog.Document.Blocks.Add(new Paragraph(new Run(display)) { Margin = new Thickness(0, 2, 0, 2), Foreground = color });
                ActivityLog.ScrollToEnd();
            });
        }

        // ── navigation ────────────────────────────────────────────────────────

        private void NavTab_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not RadioButton rb) return;
            DashboardScroller.Visibility = Visibility.Collapsed;
            PriorityScroller.Visibility  = Visibility.Collapsed;
            SettingsScroller.Visibility  = Visibility.Collapsed;
            switch (rb.Name)
            {
                case "DashboardTabBtn": DashboardScroller.Visibility = Visibility.Visible; break;
                case "PriorityTabBtn":  PriorityScroller.Visibility  = Visibility.Visible; break;
                case "SettingsTabBtn":  SettingsScroller.Visibility  = Visibility.Visible; break;
            }
        }

        // ── window events ─────────────────────────────────────────────────────

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SaveSettings();
            _reconnectTimer?.Stop();
            _sessionTimer?.Stop();
            TryAutoExportSessionLog();
            SaveOperatorState();
            _ = DisconnectBot();
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private void WindowSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        { if (TryBeginWindowDrag(e)) e.Handled = true; }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        { if (TryBeginWindowDrag(e)) e.Handled = true; }

        private bool TryBeginWindowDrag(MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || e.ClickCount > 1) return false;
            if (IsInteractiveElement(e.OriginalSource as DependencyObject)) return false;
            try { DragMove(); return true; } catch (InvalidOperationException) { return false; }
        }

        private static bool IsInteractiveElement(DependencyObject? source)
        {
            var cur = source;
            while (cur != null)
            {
                if (cur is ButtonBase || cur is TextBoxBase || cur is PasswordBox || cur is Selector ||
                    cur is RangeBase || cur is ScrollBar || cur is ScrollViewer || cur is Thumb || cur is Hyperlink) return true;
                cur = GetParentObject(cur);
            }
            return false;
        }

        private static DependencyObject? GetParentObject(DependencyObject cur)
        {
            if (cur is Visual || cur is System.Windows.Media.Media3D.Visual3D) return VisualTreeHelper.GetParent(cur);
            if (cur is FrameworkContentElement fce) return fce.Parent;
            if (cur is ContentElement ce) return ContentOperations.GetParent(ce);
            return null;
        }
    }
}