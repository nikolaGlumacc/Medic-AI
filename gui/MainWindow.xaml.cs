using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
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
    public abstract class BindableBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
            {
                return false;
            }

            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class PriorityPlayer : BindableBase
    {
        private string _statusIcon = "T1";
        private string _name = string.Empty;
        private int _tier = 1;
        private int _deaths;
        private string _followDistanceOverride = "0";
        private string _passiveModeOverride = "Inherit";

        public string StatusIcon
        {
            get => _statusIcon;
            set => SetProperty(ref _statusIcon, value);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public int Tier
        {
            get => _tier;
            set
            {
                if (SetProperty(ref _tier, value))
                {
                    StatusIcon = $"T{Math.Clamp(value, 1, 9)}";
                }
            }
        }

        public int Deaths
        {
            get => _deaths;
            set => SetProperty(ref _deaths, value);
        }

        public string FollowDistanceOverride
        {
            get => _followDistanceOverride;
            set => SetProperty(ref _followDistanceOverride, value);
        }

        public string PassiveModeOverride
        {
            get => _passiveModeOverride;
            set => SetProperty(ref _passiveModeOverride, value);
        }
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

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string PrimaryWeapon
        {
            get => _primaryWeapon;
            set => SetProperty(ref _primaryWeapon, value);
        }

        public string SecondaryWeapon
        {
            get => _secondaryWeapon;
            set => SetProperty(ref _secondaryWeapon, value);
        }

        public string MeleeWeapon
        {
            get => _meleeWeapon;
            set => SetProperty(ref _meleeWeapon, value);
        }
    }

    public class AutoLoadoutRule : BindableBase
    {
        private string _teammateClass = "Soldier";
        private LoadoutPreset? _preset;

        public string TeammateClass
        {
            get => _teammateClass;
            set => SetProperty(ref _teammateClass, value);
        }

        public LoadoutPreset? Preset
        {
            get => _preset;
            set => SetProperty(ref _preset, value);
        }
    }

    public class AudioCueSetting : BindableBase
    {
        private bool _enabled = true;
        private int _volume = 75;
        private string _customFilePath = string.Empty;

        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;

        public bool Enabled
        {
            get => _enabled;
            set => SetProperty(ref _enabled, value);
        }

        public int Volume
        {
            get => _volume;
            set => SetProperty(ref _volume, value);
        }

        public string CustomFilePath
        {
            get => _customFilePath;
            set => SetProperty(ref _customFilePath, value);
        }
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

        public bool Enabled
        {
            get => _enabled;
            set => SetProperty(ref _enabled, value);
        }

        public int Volume
        {
            get => _volume;
            set => SetProperty(ref _volume, value);
        }

        public string CustomFilePath
        {
            get => _customFilePath;
            set => SetProperty(ref _customFilePath, value);
        }

        public bool TtsFallbackEnabled
        {
            get => _ttsFallbackEnabled;
            set => SetProperty(ref _ttsFallbackEnabled, value);
        }

        public int TtsVolume
        {
            get => _ttsVolume;
            set => SetProperty(ref _ttsVolume, value);
        }
    }

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
                    if (string.IsNullOrWhiteSpace(StandardOutput))
                    {
                        return StandardError.Trim();
                    }

                    if (string.IsNullOrWhiteSpace(StandardError))
                    {
                        return StandardOutput.Trim();
                    }

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

        public ObservableCollection<PriorityPlayer> Priorities { get; }
        public ObservableCollection<RespawnTimerObj> RespawnTimers { get; }
        public ObservableCollection<string> WhitelistEntries { get; }
        public ObservableCollection<string> BlacklistEntries { get; }
        public ObservableCollection<LoadoutPreset> LoadoutPresets { get; }
        public ObservableCollection<AutoLoadoutRule> AutoLoadoutRules { get; }
        public ObservableCollection<AudioCueSetting> AudioCueSettings { get; }
        public ObservableCollection<VoiceLineSetting> VoiceLineSettings { get; }

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

        public MainWindow()
        {
            InitializeComponent();

            _repositoryRoot = FindRepositoryRoot();

            Priorities = new ObservableCollection<PriorityPlayer>();
            RespawnTimers = new ObservableCollection<RespawnTimerObj>();
            WhitelistEntries = new ObservableCollection<string>();
            BlacklistEntries = new ObservableCollection<string>();
            LoadoutPresets = new ObservableCollection<LoadoutPreset>();
            AutoLoadoutRules = new ObservableCollection<AutoLoadoutRule>();
            AudioCueSettings = new ObservableCollection<AudioCueSetting>();
            VoiceLineSettings = new ObservableCollection<VoiceLineSetting>();

            DataContext = this;

            PriorityListView.ItemsSource = Priorities;
            RespawnTimerList.ItemsSource = RespawnTimers;
            WhitelistBox.ItemsSource = WhitelistEntries;
            BlacklistBox.ItemsSource = BlacklistEntries;
            LoadoutPresetListView.ItemsSource = LoadoutPresets;
            AutoLoadoutRuleListView.ItemsSource = AutoLoadoutRules;
            AudioCueListView.ItemsSource = AudioCueSettings;
            VoiceLineListView.ItemsSource = VoiceLineSettings;

            InitializeSettingsData();
            LoadOperatorState();
            UpdateAllTimeMeleeCounter();

            SetupTimers();
            ResetSessionState();
            ApplyConnectionState(false, "Offline", Brushes.IndianRed);

            DashboardScroller.Visibility = Visibility.Visible;
            PriorityScroller.Visibility = Visibility.Collapsed;
            SettingsScroller.Visibility = Visibility.Collapsed;

            _isInitialized = true;
            _ = RefreshUpdateStatusAsync(fetchRemote: AutoCheckUpdatesOnLaunchToggle.IsChecked == true, logErrors: false);
        }

        private void InitializeSettingsData()
        {
            LoadoutPresets.Add(new LoadoutPreset
            {
                Name = "Default Medigun",
                PrimaryWeapon = "Crusader's Crossbow",
                SecondaryWeapon = "Medi Gun",
                MeleeWeapon = "Ubersaw"
            });
            LoadoutPresets.Add(new LoadoutPreset
            {
                Name = "Kritz Push",
                PrimaryWeapon = "Crusader's Crossbow",
                SecondaryWeapon = "Kritzkrieg",
                MeleeWeapon = "Ubersaw"
            });
            LoadoutPresets.Add(new LoadoutPreset
            {
                Name = "Quick-Fix Rescue",
                PrimaryWeapon = "Crusader's Crossbow",
                SecondaryWeapon = "Quick-Fix",
                MeleeWeapon = "Solemn Vow"
            });

            AutoLoadoutRules.Add(new AutoLoadoutRule
            {
                TeammateClass = "Soldier",
                Preset = LoadoutPresets.FirstOrDefault()
            });

            AddAudioCue("target_switched", "Target switched");
            AddAudioCue("uber_ready", "Uber ready");
            AddAudioCue("uber_activated", "Uber activated");
            AddAudioCue("uber_cooldown_started", "Uber cooldown started");
            AddAudioCue("spy_detected", "Spy detected");
            AddAudioCue("backstabbed", "Backstabbed");
            AddAudioCue("lost_priority_player", "Lost priority player");
            AddAudioCue("resupplied", "Resupplied");
            AddAudioCue("rejoined_after_kick", "Rejoined after kick");
            AddAudioCue("returning_to_spawn", "Returning to spawn");
            AddAudioCue("actively_searching", "Actively searching");
            AddAudioCue("priority_player_died", "Priority player died");
            AddAudioCue("whitelisted_player_joined", "Whitelisted player joined");
            AddAudioCue("threat_flagged", "Threat flagged");
            AddAudioCue("tier_1_event_tone", "Tier 1 event tone");
            AddAudioCue("tier_2_event_tone", "Tier 2 event tone");

            AddVoiceLine("spy_detected", "SPY detected");
            AddVoiceLine("backstabbed", "Backstabbed");
            AddVoiceLine("uber_ready", "Uber ready");
            AddVoiceLine("uber_activated", "Uber activated");
            AddVoiceLine("target_died", "Target died");
            AddVoiceLine("low_health", "Low health");
            AddVoiceLine("uber_saved_someone", "Uber saved someone");
            AddVoiceLine("thanks_response", "Thanks response");
            AddVoiceLine("melee_kill", "Melee kill");

            string defaultLogDirectory = Path.Combine(_repositoryRoot ?? Directory.GetCurrentDirectory(), "logs");
            Directory.CreateDirectory(defaultLogDirectory);
            LogFileSaveLocationInput.Text = defaultLogDirectory;

            if (LoadoutPresets.Count > 0)
            {
                ActivePresetSelector.SelectedIndex = 0;
            }
        }

        private void AddAudioCue(string key, string label)
        {
            AudioCueSettings.Add(new AudioCueSetting
            {
                Key = key,
                Label = label
            });
        }

        private void AddVoiceLine(string key, string label)
        {
            VoiceLineSettings.Add(new VoiceLineSetting
            {
                Key = key,
                Label = label
            });
        }

        private void LoadOperatorState()
        {
            try
            {
                string statePath = GetOperatorStatePath();
                if (!File.Exists(statePath))
                {
                    _allTimeMeleeKills = 0;
                    return;
                }

                string raw = File.ReadAllText(statePath);
                dynamic? state = JsonConvert.DeserializeObject(raw);
                _allTimeMeleeKills = state?.all_time_melee_kills != null ? (int)state.all_time_melee_kills : 0;
            }
            catch
            {
                _allTimeMeleeKills = 0;
            }
        }

        private void SaveOperatorState()
        {
            try
            {
                string statePath = GetOperatorStatePath();
                Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
                string json = JsonConvert.SerializeObject(new { all_time_melee_kills = _allTimeMeleeKills }, Formatting.Indented);
                File.WriteAllText(statePath, json);
            }
            catch
            {
                // Ignore local state persistence failures.
            }
        }

        private string GetOperatorStatePath()
        {
            string root = _repositoryRoot ?? Directory.GetCurrentDirectory();
            return Path.Combine(root, "logs", "operator_state.json");
        }

        private void UpdateAllTimeMeleeCounter()
        {
            if (AllTimeMeleeKillsText != null)
            {
                AllTimeMeleeKillsText.Text = $"All-time melee kills: {_allTimeMeleeKills}";
            }
        }

        private void SetupTimers()
        {
            _sessionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            _sessionTimer.Tick += (_, _) =>
            {
                _sessionSeconds++;
                UpdateSessionTimerText();
            };

            _reconnectTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };

            _reconnectTimer.Tick += async (_, _) =>
            {
                if (!_isConnected && AutoReconnectCB.IsChecked == true)
                {
                    await ConnectBot();
                }
            };

            _reconnectTimer.Start();
        }

        private void ResetSessionState()
        {
            _meleeKills = 0;
            _totalHealing = 0;
            _sessionSeconds = 0;
            _sessionLogExported = false;
            FollowModeLabel.Text = "Mode: STANDBY";
            UberMeterLabel.Text = "Uber: 0%";
            UpdateSessionStats();
            UpdateSessionTimerText();
            UpdateActionButtons();
        }

        private void UpdateSessionTimerText()
        {
            int hours = _sessionSeconds / 3600;
            int minutes = (_sessionSeconds % 3600) / 60;
            int seconds = _sessionSeconds % 60;
            SessionTimer.Text = $"{hours:D2}:{minutes:D2}:{seconds:D2}";
        }

        private void ApplyConnectionState(bool connected, string statusText, Brush statusBrush)
        {
            _isConnected = connected;
            StatusLabel.Text = statusText;
            StatusLabel.Foreground = statusBrush;
            StatusDot.Fill = statusBrush;
            StatusStripDot.Fill = statusBrush;
            ConnectBtn.Content = connected ? "Disconnect" : "Connect";

            if (!connected)
            {
                _isBotRunning = false;
                FollowModeLabel.Text = "Mode: STANDBY";
                _sessionTimer?.Stop();
            }

            UpdateActionButtons();
        }

        private void UpdateActionButtons()
        {
            StartBtn.IsEnabled = _isConnected && !_isBotRunning;
            StopBtn.IsEnabled = _isConnected && _isBotRunning;
            SendConfigBtn.IsEnabled = _isConnected;
        }

        private string GetSelectedComboContent(ComboBox comboBox, string fallback)
        {
            if (comboBox.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                return item.Content.ToString() ?? fallback;
            }

            if (comboBox.SelectedItem != null)
            {
                return comboBox.SelectedItem.ToString() ?? fallback;
            }

            return string.IsNullOrWhiteSpace(comboBox.Text) ? fallback : comboBox.Text.Trim();
        }

        private LoadoutPreset GetActivePreset()
        {
            return ActivePresetSelector.SelectedItem as LoadoutPreset
                   ?? LoadoutPresets.FirstOrDefault()
                   ?? new LoadoutPreset
                   {
                       Name = "Default Medigun",
                       PrimaryWeapon = "Crusader's Crossbow",
                       SecondaryWeapon = "Medi Gun",
                       MeleeWeapon = "Ubersaw"
                   };
        }

        private string GetDefaultModeOnStartup()
        {
            return GetSelectedComboContent(DefaultModeOnStartupCombo, "Active");
        }

        private string GetUberBehaviorMode()
        {
            if (ManualOnlyToggle.IsChecked == true)
            {
                return "Manual";
            }

            if (AutoPopToggle.IsChecked == true)
            {
                return "Auto-pop";
            }

            if (UberSuggestionAudioCueToggle.IsChecked == true)
            {
                return "Suggest";
            }

            return "Manual";
        }

        private static int ParseIntOrDefault(string? text, int fallback = 0)
        {
            return int.TryParse(text, out int value) ? value : fallback;
        }

        private object BuildBotConfigPayload()
        {
            LoadoutPreset activePreset = GetActivePreset();

            return new
            {
                connection = new
                {
                    laptop_ip_address = string.IsNullOrWhiteSpace(BotIpInput.Text) ? "127.0.0.1" : BotIpInput.Text.Trim(),
                    port_number = string.IsNullOrWhiteSpace(PortInput.Text) ? "8765" : PortInput.Text.Trim(),
                    auto_reconnect = AutoReconnectCB.IsChecked == true,
                    connection_timeout_duration = (int)ConnectionTimeoutSlider.Value
                },
                primary_weapon = activePreset.PrimaryWeapon,
                secondary_weapon = activePreset.SecondaryWeapon,
                melee_weapon = activePreset.MeleeWeapon,
                active_preset = activePreset.Name,
                loadout_presets = LoadoutPresets.Select(preset => new
                {
                    name = preset.Name,
                    primary_weapon = preset.PrimaryWeapon,
                    secondary_weapon = preset.SecondaryWeapon,
                    melee_weapon = preset.MeleeWeapon
                }).ToList(),
                auto_loadout_rules = AutoLoadoutRules.Select(rule => new
                {
                    teammate_class = rule.TeammateClass,
                    equip_preset = rule.Preset?.Name ?? string.Empty
                }).ToList(),
                follow_distance = (int)FollowDistanceSlider.Value,
                follow_behavior = new
                {
                    default_follow_distance = (int)FollowDistanceSlider.Value,
                    active_mode_follow_distance = (int)ActiveModeFollowDistanceSlider.Value,
                    passive_mode_follow_distance = (int)PassiveModeFollowDistanceSlider.Value,
                    escort_mode_follow_distance = (int)EscortModeFollowDistanceSlider.Value,
                    erratic_movement_threshold = (int)ErraticMovementThresholdSlider.Value,
                    search_mode_timeout_before_returning_to_spawn = (int)SearchModeTimeoutSlider.Value,
                    stuck_detection_sensitivity = (int)StuckDetectionSensitivitySlider.Value,
                    stuck_detection_retry_attempts = (int)StuckDetectionRetrySlider.Value,
                    ledge_correction_sensitivity = (int)LedgeCorrectionSensitivitySlider.Value
                },
                uber_behavior = GetUberBehaviorMode(),
                uber = new
                {
                    auto_pop = AutoPopToggle.IsChecked == true,
                    auto_pop_health_threshold = (int)AutoPopHealthThresholdSlider.Value,
                    manual_only = ManualOnlyToggle.IsChecked == true,
                    uber_suggestion_audio_cue = UberSuggestionAudioCueToggle.IsChecked == true,
                    hold_uber_if_priority_is_dead = HoldUberIfPriorityDeadToggle.IsChecked == true,
                    defensive_uber = DefensiveUberToggle.IsChecked == true
                },
                spy_check_frequency = (int)SpyCheckFreqSlider.Value,
                spy_detection = new
                {
                    spy_check_frequency = (int)SpyCheckFreqSlider.Value,
                    spy_alertness_duration_after_backstab = (int)SpyAlertnessDurationSlider.Value,
                    spy_alertness_increase_after_kill_feed_spy_death = SpyAlertnessIncreaseAfterKillFeedToggle.IsChecked == true,
                    spy_check_camera_flick_speed = (int)SpyCheckCameraFlickSpeedSlider.Value
                },
                scanning = new
                {
                    idle_rotation_speed = (int)IdleRotationSpeedSlider.Value,
                    rotation_speed_when_callout_detected = (int)CalloutRotationSpeedSlider.Value,
                    rotation_degrees_per_step = (int)RotationDegreesPerStepSlider.Value,
                    yolo_confidence_threshold = (int)YoloConfidenceThresholdSlider.Value,
                    ocr_sensitivity = (int)OcrSensitivitySlider.Value,
                    search_mode_rotation_speed = (int)SearchModeRotationSpeedSlider.Value
                },
                melee_mode_enabled = MeleeModeToggle.IsChecked == true,
                melee_mode = new
                {
                    enabled = MeleeModeToggle.IsChecked == true,
                    outnumbered_retreat_threshold = (int)OutnumberedRetreatThresholdSlider.Value,
                    critical_health_retreat_threshold = (int)CriticalHealthRetreatThresholdSlider.Value,
                    melee_kill_voice_line = MeleeKillVoiceLineToggle.IsChecked == true,
                    melee_kill_logging = MeleeKillLoggingToggle.IsChecked == true
                },
                passive_mode_enabled = string.Equals(GetDefaultModeOnStartup(), "Passive", StringComparison.OrdinalIgnoreCase),
                passive_mode = new
                {
                    default_mode_on_startup = GetDefaultModeOnStartup(),
                    double_callout_timeout = Math.Round(DoubleCalloutTimeoutSlider.Value, 1),
                    idle_rotation_speed = (int)PassiveModeIdleRotationSpeedSlider.Value,
                    enemy_avoidance = PassiveModeEnemyAvoidanceToggle.IsChecked == true,
                    idle_audio_cue = PassiveIdleAudioCueToggle.IsChecked == true,
                    idle_audio_cue_interval = (int)PassiveIdleAudioCueIntervalSlider.Value
                },
                master_volume = (int)MasterVolumeSlider.Value,
                audio_cues = AudioCueSettings.Select(setting => new
                {
                    key = setting.Key,
                    label = setting.Label,
                    enabled = setting.Enabled,
                    volume = setting.Volume,
                    custom_file = setting.CustomFilePath
                }).ToList(),
                master_voice_line_enabled = MasterVoiceLineToggle.IsChecked == true,
                voice_lines = VoiceLineSettings.Select(setting => new
                {
                    key = setting.Key,
                    label = setting.Label,
                    enabled = setting.Enabled,
                    volume = setting.Volume,
                    custom_file = setting.CustomFilePath,
                    tts_fallback = setting.TtsFallbackEnabled,
                    tts_volume = setting.TtsVolume
                }).ToList(),
                scoreboard_checks = new
                {
                    scoreboard_check_frequency = (int)ScoreboardCheckFrequencySlider.Value,
                    tab_hold_duration = Math.Round(TabHoldDurationSlider.Value, 2),
                    auto_update_whitelist_from_scoreboard = AutoUpdateWhitelistFromScoreboardToggle.IsChecked == true,
                    auto_whitelist_detection_from_scoreboard = AutoWhitelistDetectionCheckBox.IsChecked == true,
                    class_detection = ClassDetectionToggle.IsChecked == true
                },
                session_logging = new
                {
                    auto_export_log_after_session = AutoExportLogAfterSessionToggle.IsChecked == true,
                    log_file_save_location = LogFileSaveLocationInput.Text.Trim(),
                    weekly_summary = WeeklySummaryToggle.IsChecked == true,
                    weekly_summary_day = GetSelectedComboContent(WeeklySummaryDayCombo, "Monday"),
                    timestamps_in_log = TimestampsInLogToggle.IsChecked == true,
                    disconnect_and_kick_logging = DisconnectKickLoggingToggle.IsChecked == true,
                    melee_kill_all_time_counter = _allTimeMeleeKills
                },
                performance = new
                {
                    screen_capture_fps_limit = (int)ScreenCaptureFpsLimitSlider.Value,
                    yolo_thread_priority = GetSelectedComboContent(YoloThreadPriorityCombo, "Normal"),
                    cpu_temperature_throttle_threshold = (int)CpuThrottleThresholdSlider.Value,
                    throttle_amount_when_overheating = (int)ThrottleAmountSlider.Value,
                    watchdog_auto_restart = WatchdogAutoRestartToggle.IsChecked == true,
                    tf2_window_lock = Tf2WindowLockToggle.IsChecked == true
                },
                startup_shutdown = new
                {
                    auto_skip_tf2_intro = AutoSkipTf2IntroToggle.IsChecked == true,
                    auto_select_medic = AutoSelectMedicToggle.IsChecked == true,
                    auto_equip_loadout_on_start = AutoEquipLoadoutOnStartToggle.IsChecked == true,
                    clean_shutdown_behavior = GetSelectedComboContent(CleanShutdownBehaviorCombo, "Idle in spawn"),
                    watchdog_restart_delay = (int)WatchdogRestartDelaySlider.Value
                },
                updates = new
                {
                    auto_check_on_launch = AutoCheckUpdatesOnLaunchToggle.IsChecked == true
                },
                priority_list = Priorities.Select(player => new
                {
                    name = player.Name,
                    tier = player.Tier,
                    follow_distance_override = ParseIntOrDefault(player.FollowDistanceOverride),
                    passive_mode_override = player.PassiveModeOverride
                }).ToList(),
                whitelist = WhitelistEntries.ToList(),
                blacklist = BlacklistEntries.ToList()
            };
        }

        private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected)
            {
                await DisconnectBot();
            }
            else
            {
                await ConnectBot();
            }
        }

        private Uri BuildBotUri()
        {
            string ip = string.IsNullOrWhiteSpace(BotIpInput.Text) ? "127.0.0.1" : BotIpInput.Text.Trim();
            string port = string.IsNullOrWhiteSpace(PortInput.Text) ? "8765" : PortInput.Text.Trim();
            return new Uri($"ws://{ip}:{port}");
        }

        private async void TestConnectionBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using ClientWebSocket probeSocket = new();
                using CancellationTokenSource probeTimeout = new(TimeSpan.FromSeconds((int)ConnectionTimeoutSlider.Value));
                Uri endpoint = BuildBotUri();
                await probeSocket.ConnectAsync(endpoint, probeTimeout.Token);

                if (probeSocket.State == WebSocketState.Open)
                {
                    await probeSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Probe complete", CancellationToken.None);
                }

                AppendActivity($"[OK] Test connection succeeded: {endpoint.Host}:{endpoint.Port}");
            }
            catch (Exception ex)
            {
                AppendActivity($"[ERR] Test connection failed: {ex.Message}", true);
            }
        }

        private async Task ConnectBot()
        {
            if (_isConnected)
            {
                return;
            }

            ApplyConnectionState(false, "Linking...", Brushes.Goldenrod);

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _ws?.Dispose();
            _ws = new ClientWebSocket();
            Uri endpoint = BuildBotUri();

            try
            {
                using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
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
                _isConnected = false;
                _ws?.Dispose();
                _ws = null;
                ApplyConnectionState(false, "Offline", Brushes.IndianRed);
                AppendActivity("[ERR] Connection attempt timed out.", true);
            }
            catch (Exception ex)
            {
                _isConnected = false;
                _ws?.Dispose();
                _ws = null;
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
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None);
                }

                _ws?.Dispose();
                _ws = null;
            }
            catch
            {
                // Ignore disconnect errors.
            }
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
                    await using var memoryStream = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        if (_cts == null)
                        {
                            return;
                        }

                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                            Dispatcher.Invoke(SetDisconnectedUi);
                            return;
                        }

                        memoryStream.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    string message = Encoding.UTF8.GetString(memoryStream.ToArray());

                    try
                    {
                        dynamic? data = JsonConvert.DeserializeObject(message);
                        if (data != null)
                        {
                            Dispatcher.Invoke(() => HandleBotMessage(data));
                        }
                    }
                    catch
                    {
                        // Ignore malformed JSON payloads.
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during disconnect.
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    SetDisconnectedUi();
                    AppendActivity($"[ERR] Bot link dropped: {ex.Message}", true);
                });
            }
        }

        private void HandleBotMessage(dynamic data)
        {
            string type = (string)data["type"];

            if (type == "activity")
            {
                string message = (string)data["msg"];
                bool isMeleeKill = message.Contains("melee kill", StringComparison.OrdinalIgnoreCase);
                bool isError = message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                               message.Contains("failed", StringComparison.OrdinalIgnoreCase);

                if (isMeleeKill)
                {
                    _meleeKills++;
                    _allTimeMeleeKills++;
                    UpdateSessionStats();
                    UpdateAllTimeMeleeCounter();
                    SaveOperatorState();

                    if (MeleeKillLoggingToggle.IsChecked != true)
                    {
                        return;
                    }
                }

                AppendActivity(message, isError);
                return;
            }

            if (type == "status")
            {
                if (data["uber"] != null)
                {
                    UberMeterLabel.Text = $"Uber: {data["uber"]}%";
                }

                if (data["mode"] != null)
                {
                    FollowModeLabel.Text = $"Mode: {((string)data["mode"]).ToUpperInvariant()}";
                }
            }
        }

        private void SetDisconnectedUi()
        {
            ApplyConnectionState(false, "Offline", Brushes.IndianRed);
        }

        private async Task SendJson(object payload)
        {
            if (_ws?.State != WebSocketState.Open || _cts == null)
            {
                return;
            }

            string json = JsonConvert.SerializeObject(payload);
            byte[] data = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, _cts.Token);
        }

        private async void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected)
            {
                AppendActivity("[ERR] Connect to the bot before deploying.", true);
                return;
            }

            try
            {
                await SendJson(new
                {
                    type = "config",
                    config = BuildBotConfigPayload()
                });

                await SendJson(new { type = "start" });

                _isBotRunning = true;
                _meleeKills = 0;
                _totalHealing = 0;
                _sessionSeconds = 0;
                _sessionLogExported = false;
                _sessionTimer?.Start();
                UpdateSessionStats();
                FollowModeLabel.Text = string.Equals(GetDefaultModeOnStartup(), "Passive", StringComparison.OrdinalIgnoreCase)
                    ? "Mode: PASSIVE"
                    : "Mode: ACTIVE";
                UpdateActionButtons();
                AppendActivity("[OK] Medic bot deployed.");
            }
            catch (Exception ex)
            {
                AppendActivity($"[ERR] Deploy failed: {ex.Message}", true);
            }
        }

        private async void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected)
            {
                AppendActivity("[ERR] Bot link is offline.", true);
                return;
            }

            try
            {
                await SendJson(new { type = "stop" });
                _isBotRunning = false;
                _sessionTimer?.Stop();
                FollowModeLabel.Text = "Mode: STANDBY";
                UpdateActionButtons();
                AppendActivity("[INFO] Stop command sent to bot.");
                TryAutoExportSessionLog();
            }
            catch (Exception ex)
            {
                AppendActivity($"[ERR] Stop failed: {ex.Message}", true);
            }
        }

        private async void SendConfigBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected)
            {
                AppendActivity("[ERR] Connect to the bot before syncing config.", true);
                return;
            }

            try
            {
                await SendJson(new
                {
                    type = "config",
                    config = BuildBotConfigPayload()
                });

                AppendActivity("[SYNC] Configuration pushed to bot.");
            }
            catch (Exception ex)
            {
                AppendActivity($"[ERR] Config sync failed: {ex.Message}", true);
            }
        }

        private void AddPriority_Click(object sender, RoutedEventArgs e)
        {
            string name = PriorityNameInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (Priorities.Any(player => player.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                AppendActivity($"[WARN] {name} is already marked as priority.", true);
                return;
            }

            int tier = TierInput.SelectedItem is int selectedTier ? selectedTier : 1;
            tier = Math.Clamp(tier, 1, 3);
            Priorities.Add(new PriorityPlayer
            {
                Name = name,
                Tier = tier,
                Deaths = 0,
                FollowDistanceOverride = "0",
                PassiveModeOverride = "Inherit"
            });
            PriorityNameInput.Clear();
            TierInput.SelectedIndex = 0;
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
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            PriorityPlayer? existing = Priorities.FirstOrDefault(player => player.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Tier = tier;
                return;
            }

            Priorities.Add(new PriorityPlayer
            {
                Name = name,
                Tier = tier,
                Deaths = 0,
                FollowDistanceOverride = "0",
                PassiveModeOverride = "Inherit"
            });
        }

        private void RemovePriority_Click(object sender, RoutedEventArgs e)
        {
            if (PriorityListView.SelectedItem is PriorityPlayer player)
            {
                Priorities.Remove(player);
                AppendActivity($"[INFO] Removed priority target: {player.Name}.");
                return;
            }

            AppendActivity("[ERR] Select a priority target first.", true);
        }

        private void MovePriorityUp_Click(object sender, RoutedEventArgs e)
        {
            if (PriorityListView.SelectedItem is not PriorityPlayer player)
            {
                AppendActivity("[ERR] Select a priority target first.", true);
                return;
            }

            int index = Priorities.IndexOf(player);
            if (index <= 0)
            {
                return;
            }

            Priorities.Move(index, index - 1);
            PriorityListView.SelectedItem = player;
        }

        private void MovePriorityDown_Click(object sender, RoutedEventArgs e)
        {
            if (PriorityListView.SelectedItem is not PriorityPlayer player)
            {
                AppendActivity("[ERR] Select a priority target first.", true);
                return;
            }

            int index = Priorities.IndexOf(player);
            if (index < 0 || index >= Priorities.Count - 1)
            {
                return;
            }

            Priorities.Move(index, index + 1);
            PriorityListView.SelectedItem = player;
        }

        private void AddLoadoutRule_Click(object sender, RoutedEventArgs e)
        {
            AutoLoadoutRules.Add(new AutoLoadoutRule
            {
                TeammateClass = TeammateClassOptions.First(),
                Preset = GetActivePreset()
            });
        }

        private void RemoveLoadoutRule_Click(object sender, RoutedEventArgs e)
        {
            if (AutoLoadoutRuleListView.SelectedItem is AutoLoadoutRule rule)
            {
                AutoLoadoutRules.Remove(rule);
                return;
            }

            AppendActivity("[ERR] Select a loadout rule first.", true);
        }

        private void AddWhitelist_Click(object sender, RoutedEventArgs e)
        {
            string name = WhitelistInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (WhitelistEntries.Any(entry => entry.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                AppendActivity($"[WARN] {name} is already whitelisted.", true);
                return;
            }

            WhitelistEntries.Add(name);
            WhitelistInput.Clear();
            AppendActivity($"[OK] Whitelisted {name}.");
        }

        private void RemoveWhitelist_Click(object sender, RoutedEventArgs e)
        {
            if (WhitelistBox.SelectedItem is string name)
            {
                WhitelistEntries.Remove(name);
                AppendActivity($"[INFO] Removed {name} from whitelist.");
            }
        }

        private void AddBlacklist_Click(object sender, RoutedEventArgs e)
        {
            string name = BlacklistInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (BlacklistEntries.Any(entry => entry.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                AppendActivity($"[WARN] {name} is already blacklisted.", true);
                return;
            }

            BlacklistEntries.Add(name);
            BlacklistInput.Clear();
            AppendActivity($"[OK] Blacklisted {name}.");
        }

        private void RemoveBlacklist_Click(object sender, RoutedEventArgs e)
        {
            if (BlacklistBox.SelectedItem is string name)
            {
                BlacklistEntries.Remove(name);
                AppendActivity($"[INFO] Removed {name} from blacklist.");
            }
        }

        private void QuickBanBtn_Click(object sender, RoutedEventArgs e)
        {
            string name = BlacklistInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                AppendActivity("[ERR] Enter a name in the blacklist field first.", true);
                return;
            }

            if (!BlacklistEntries.Any(entry => entry.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                BlacklistEntries.Add(name);
            }

            BlacklistInput.Clear();
            AppendActivity($"[WARN] Quick banned {name} for the current match.");
        }

        private void ResetStatsBtn_Click(object sender, RoutedEventArgs e)
        {
            ResetSessionState();
            AppendActivity("[INFO] Session stats reset.");
        }

        private void UpdateSessionStats()
        {
            MeleeKillsStatLabel.Text = $"Melee picks: {_meleeKills}";
            TotalHealingStatLabel.Text = $"Healing output: {_totalHealing:N0} HP";
            UpdateAllTimeMeleeCounter();
        }

        private void OpenSoundFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            string basePath = _repositoryRoot ?? Directory.GetCurrentDirectory();
            string soundFolder = Path.Combine(basePath, "assets", "sounds");
            Directory.CreateDirectory(soundFolder);

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{soundFolder}\"",
                UseShellExecute = true
            });

            AppendActivity($"[OK] Opened sound folder: {soundFolder}");
        }

        private void UseDefaultLogLocationBtn_Click(object sender, RoutedEventArgs e)
        {
            string defaultLogDirectory = Path.Combine(_repositoryRoot ?? Directory.GetCurrentDirectory(), "logs");
            Directory.CreateDirectory(defaultLogDirectory);
            LogFileSaveLocationInput.Text = defaultLogDirectory;
            AppendActivity($"[OK] Log location reset to {defaultLogDirectory}");
        }

        private void ExportActivityLogBtn_Click(object sender, RoutedEventArgs e)
        {
            ExportActivityLog(showMessage: true);
        }

        private void TryAutoExportSessionLog()
        {
            if (_sessionLogExported ||
                AutoExportLogAfterSessionToggle.IsChecked != true ||
                _sessionSeconds <= 0)
            {
                return;
            }

            ExportActivityLog(showMessage: true);
            _sessionLogExported = true;
        }

        private void ExportActivityLog(bool showMessage)
        {
            try
            {
                string directory = string.IsNullOrWhiteSpace(LogFileSaveLocationInput.Text)
                    ? Path.Combine(_repositoryRoot ?? Directory.GetCurrentDirectory(), "logs")
                    : LogFileSaveLocationInput.Text.Trim();

                Directory.CreateDirectory(directory);

                string filePath = Path.Combine(directory, $"medicai_session_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                TextRange range = new(ActivityLog.Document.ContentStart, ActivityLog.Document.ContentEnd);
                File.WriteAllText(filePath, range.Text.Trim());

                if (showMessage)
                {
                    AppendActivity($"[OK] Log exported to {filePath}");
                }
            }
            catch (Exception ex)
            {
                AppendActivity($"[ERR] Log export failed: {ex.Message}", true);
            }
        }

        private void UploadAudioFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                Filter = "Audio Files|*.wav;*.mp3;*.wma;*.aac;*.m4a|All Files|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            string defaultSoundDirectory = Path.Combine(_repositoryRoot ?? Directory.GetCurrentDirectory(), "assets", "sounds");
            if (Directory.Exists(defaultSoundDirectory))
            {
                dialog.InitialDirectory = defaultSoundDirectory;
            }

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            switch ((sender as FrameworkElement)?.DataContext)
            {
                case AudioCueSetting audioCue:
                    audioCue.CustomFilePath = dialog.FileName;
                    AppendActivity($"[OK] Attached custom audio cue for {audioCue.Label}.");
                    break;
                case VoiceLineSetting voiceLine:
                    voiceLine.CustomFilePath = dialog.FileName;
                    AppendActivity($"[OK] Attached custom voice line for {voiceLine.Label}.");
                    break;
            }
        }

        private void PreviewVoiceLine_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not VoiceLineSetting voiceLine)
            {
                return;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(voiceLine.CustomFilePath) && File.Exists(voiceLine.CustomFilePath))
                {
                    _previewPlayer.Open(new Uri(voiceLine.CustomFilePath, UriKind.Absolute));
                    _previewPlayer.Volume = voiceLine.Volume / 100d;
                    _previewPlayer.Play();
                    AppendActivity($"[OK] Previewing voice line: {voiceLine.Label}");
                    return;
                }

                SystemSounds.Asterisk.Play();
                AppendActivity($"[INFO] No custom file assigned for {voiceLine.Label}; played fallback preview.");
            }
            catch (Exception ex)
            {
                AppendActivity($"[ERR] Voice line preview failed: {ex.Message}", true);
            }
        }

        private async void CheckUpdatesBtn_Click(object sender, RoutedEventArgs e)
        {
            await RunUpdateOperationAsync(async () =>
            {
                AppendActivity("[UPD] Checking remote for updates...");
                await RefreshUpdateStatusAsync(fetchRemote: true, logErrors: true);
            });
        }

        private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            await RunUpdateOperationAsync(async () =>
            {
                UpdateSnapshot snapshot = await GetUpdateSnapshotAsync(fetchRemote: true, CancellationToken.None);
                ApplyUpdateSnapshot(snapshot);

                if (!snapshot.IsRepositoryAvailable)
                {
                    AppendActivity("[ERR] In-app updating needs a git clone with a configured remote.", true);
                    return;
                }

                if (snapshot.WorktreeDirty)
                {
                    AppendActivity("[ERR] Update blocked because the repo has local changes. Commit or stash them first.", true);
                    return;
                }

                if (snapshot.BehindCount <= 0)
                {
                    AppendActivity("[OK] MedicAI is already on the latest remote commit.");
                    return;
                }

                AppendActivity($"[UPD] Pulling {snapshot.BehindCount} update(s) from origin/{snapshot.Branch}...");
                ProcessResult pull = await RunProcessAsync(
                    "git",
                    $"pull --ff-only origin {snapshot.Branch}",
                    _repositoryRoot!,
                    CancellationToken.None,
                    90000);

                if (!pull.Succeeded)
                {
                    AppendActivity($"[ERR] Update install failed: {FirstMeaningfulLine(pull.CombinedOutput)}", true);
                    await RefreshUpdateStatusAsync(fetchRemote: false, logErrors: false);
                    return;
                }

                AppendActivity("[OK] Update installed. MedicAI can relaunch through start.bat now.");
                await RefreshUpdateStatusAsync(fetchRemote: false, logErrors: false);

                MessageBoxResult relaunchChoice = MessageBox.Show(
                    this,
                    "The latest MedicAI source has been pulled from the remote repository.\n\nRelaunch with start.bat now?",
                    "MedicAI Updated",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (relaunchChoice == MessageBoxResult.Yes)
                {
                    RelaunchFromStartScript();
                }
            });
        }

        private async void RollbackBtn_Click(object sender, RoutedEventArgs e)
        {
            await RunUpdateOperationAsync(async () =>
            {
                UpdateSnapshot snapshot = await GetUpdateSnapshotAsync(fetchRemote: false, CancellationToken.None);
                ApplyUpdateSnapshot(snapshot);

                if (!snapshot.IsRepositoryAvailable)
                {
                    AppendActivity("[ERR] Rollback needs a git clone launched from the repository.", true);
                    return;
                }

                if (snapshot.WorktreeDirty)
                {
                    AppendActivity("[ERR] Rollback blocked because the repo has local changes. Commit or stash them first.", true);
                    return;
                }

                MessageBoxResult confirm = MessageBox.Show(
                    this,
                    "Rollback will hard reset the local clone to the previous commit.\n\nContinue?",
                    "Confirm Rollback",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }

                ProcessResult rollback = await RunProcessAsync(
                    "git",
                    "reset --hard HEAD~1",
                    _repositoryRoot!,
                    CancellationToken.None,
                    90000);

                if (!rollback.Succeeded)
                {
                    AppendActivity($"[ERR] Rollback failed: {FirstMeaningfulLine(rollback.CombinedOutput)}", true);
                    await RefreshUpdateStatusAsync(fetchRemote: false, logErrors: false);
                    return;
                }

                AppendActivity("[OK] Rolled back to the previous commit.");
                await RefreshUpdateStatusAsync(fetchRemote: false, logErrors: false);
            });
        }

        private async Task RunUpdateOperationAsync(Func<Task> operation)
        {
            if (_isUpdateOperationRunning)
            {
                return;
            }

            _isUpdateOperationRunning = true;
            SetUpdateUiBusy(true);

            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                AppendActivity($"[ERR] Update workflow failed: {ex.Message}", true);
            }
            finally
            {
                _isUpdateOperationRunning = false;
                SetUpdateUiBusy(false);
            }
        }

        private void SetUpdateUiBusy(bool isBusy)
        {
            CheckUpdatesBtn.IsEnabled = !isBusy;
            if (isBusy)
            {
                UpdateBtn.IsEnabled = false;
                RollbackBtn.IsEnabled = false;
                CheckUpdatesBtn.Content = "Checking...";
                UpdateBtn.Content = "Working...";
                return;
            }

            CheckUpdatesBtn.Content = "Check Remote";

            if (UpdateBtn.Tag is string label && !string.IsNullOrWhiteSpace(label))
            {
                UpdateBtn.Content = label;
            }
        }

        private async Task RefreshUpdateStatusAsync(bool fetchRemote, bool logErrors)
        {
            try
            {
                UpdateSnapshot snapshot = await GetUpdateSnapshotAsync(fetchRemote, CancellationToken.None);
                ApplyUpdateSnapshot(snapshot);
                await RefreshChangelogAsync();
            }
            catch (Exception ex)
            {
                SetUpdateStatus(
                    "Update status unavailable",
                    FirstMeaningfulLine(ex.Message),
                    _repositoryRoot ?? "No repository detected");

                if (logErrors)
                {
                    AppendActivity($"[ERR] Could not read update state: {ex.Message}", true);
                }
            }
        }

        private void ApplyUpdateSnapshot(UpdateSnapshot snapshot)
        {
            string versionText = snapshot.IsRepositoryAvailable
                ? $"{snapshot.Branch} @ {snapshot.Commit}"
                : "Standalone build";

            SetUpdateStatus(snapshot.Summary, snapshot.Detail, snapshot.RepoPath);
            VersionValueText.Text = versionText;

            string updateButtonLabel = snapshot.BehindCount > 0
                ? $"Install {snapshot.BehindCount} Update{(snapshot.BehindCount == 1 ? string.Empty : "s")}"
                : "Install Update";

            UpdateBtn.Tag = updateButtonLabel;
            if (!_isUpdateOperationRunning)
            {
                UpdateBtn.Content = updateButtonLabel;
            }

            if (_isUpdateOperationRunning)
            {
                return;
            }

            UpdateBtn.IsEnabled = snapshot.IsRepositoryAvailable &&
                                  !snapshot.WorktreeDirty &&
                                  snapshot.BehindCount > 0;
            RollbackBtn.IsEnabled = snapshot.IsRepositoryAvailable && !snapshot.WorktreeDirty;
        }

        private void SetUpdateStatus(string summary, string detail, string repoPath)
        {
            UpdateStateText.Text = summary;
            UpdateHintText.Text = detail;
            RepoPathText.Text = repoPath;
        }

        private async Task RefreshChangelogAsync()
        {
            if (string.IsNullOrWhiteSpace(_repositoryRoot))
            {
                ChangelogViewer.Text = "Changelog unavailable outside a git clone.";
                return;
            }

            ProcessResult changelog = await RunProcessAsync(
                "git",
                "log --oneline -n 12",
                _repositoryRoot,
                CancellationToken.None);

            ChangelogViewer.Text = changelog.Succeeded && !string.IsNullOrWhiteSpace(changelog.StandardOutput)
                ? changelog.StandardOutput.Trim()
                : FirstMeaningfulLine(changelog.CombinedOutput);
        }

        private async Task<UpdateSnapshot> GetUpdateSnapshotAsync(bool fetchRemote, CancellationToken cancellationToken)
        {
            string repoPath = _repositoryRoot ?? AppContext.BaseDirectory;

            if (string.IsNullOrWhiteSpace(_repositoryRoot))
            {
                return new UpdateSnapshot
                {
                    IsRepositoryAvailable = false,
                    Summary = "Update controls need a git clone",
                    Detail = "This build could not find the MedicAI repository root.",
                    RepoPath = repoPath
                };
            }

            ProcessResult gitVersion = await RunProcessAsync("git", "--version", _repositoryRoot, cancellationToken);
            if (!gitVersion.Succeeded)
            {
                return new UpdateSnapshot
                {
                    IsRepositoryAvailable = false,
                    Summary = "Git is not available",
                    Detail = FirstMeaningfulLine(gitVersion.CombinedOutput),
                    RepoPath = repoPath
                };
            }

            ProcessResult repoCheck = await RunProcessAsync("git", "rev-parse --is-inside-work-tree", _repositoryRoot, cancellationToken);
            if (!repoCheck.Succeeded || !repoCheck.StandardOutput.Contains("true", StringComparison.OrdinalIgnoreCase))
            {
                return new UpdateSnapshot
                {
                    IsRepositoryAvailable = false,
                    Summary = "Current build is not inside a git worktree",
                    Detail = "In-app updates are only available when MedicAI is launched from the repository.",
                    RepoPath = repoPath
                };
            }

            if (fetchRemote)
            {
                ProcessResult fetch = await RunProcessAsync(
                    "git",
                    "fetch origin --prune",
                    _repositoryRoot,
                    cancellationToken,
                    90000);

                if (!fetch.Succeeded)
                {
                    return new UpdateSnapshot
                    {
                        IsRepositoryAvailable = true,
                        Summary = "Remote check failed",
                        Detail = FirstMeaningfulLine(fetch.CombinedOutput),
                        RepoPath = repoPath
                    };
                }
            }

            ProcessResult branchResult = await RunProcessAsync("git", "branch --show-current", _repositoryRoot, cancellationToken);
            ProcessResult commitResult = await RunProcessAsync("git", "rev-parse --short HEAD", _repositoryRoot, cancellationToken);
            ProcessResult worktreeResult = await RunProcessAsync("git", "status --porcelain", _repositoryRoot, cancellationToken);
            ProcessResult upstreamResult = await RunProcessAsync(
                "git",
                "rev-parse --abbrev-ref --symbolic-full-name HEAD@{upstream}",
                _repositoryRoot,
                cancellationToken);

            string branch = string.IsNullOrWhiteSpace(branchResult.StandardOutput) ? "main" : branchResult.StandardOutput.Trim();
            string commit = string.IsNullOrWhiteSpace(commitResult.StandardOutput) ? "unknown" : commitResult.StandardOutput.Trim();
            bool worktreeDirty = !string.IsNullOrWhiteSpace(worktreeResult.StandardOutput);

            string upstream = upstreamResult.Succeeded && !string.IsNullOrWhiteSpace(upstreamResult.StandardOutput)
                ? upstreamResult.StandardOutput.Trim()
                : $"origin/{branch}";

            ProcessResult upstreamVerify = await RunProcessAsync(
                "git",
                $"rev-parse --verify {upstream}",
                _repositoryRoot,
                cancellationToken);

            if (!upstreamVerify.Succeeded)
            {
                return new UpdateSnapshot
                {
                    IsRepositoryAvailable = true,
                    Branch = branch,
                    Commit = commit,
                    WorktreeDirty = worktreeDirty,
                    Summary = worktreeDirty ? "Remote not configured and local edits detected" : "Remote tracking is not configured",
                    Detail = "Set an upstream branch for this clone to enable update checks and installs.",
                    RepoPath = repoPath
                };
            }

            ProcessResult divergenceResult = await RunProcessAsync(
                "git",
                $"rev-list --left-right --count HEAD...{upstream}",
                _repositoryRoot,
                cancellationToken);

            int aheadCount = 0;
            int behindCount = 0;

            if (divergenceResult.Succeeded)
            {
                string[] parts = divergenceResult.StandardOutput
                    .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 2)
                {
                    int.TryParse(parts[0], out aheadCount);
                    int.TryParse(parts[1], out behindCount);
                }
            }

            string summary;
            string detail;

            if (behindCount > 0 && worktreeDirty)
            {
                summary = $"{behindCount} update(s) ready, but local edits are blocking install";
                detail = "Commit or stash your local changes before using Install Update.";
            }
            else if (behindCount > 0)
            {
                summary = $"{behindCount} update(s) available";
                detail = "Install Update will run git pull --ff-only, then offer a relaunch through start.bat.";
            }
            else if (aheadCount > 0 && worktreeDirty)
            {
                summary = $"Local branch is {aheadCount} commit(s) ahead and has uncommitted edits";
                detail = "This clone is already ahead of origin, so there is nothing to install from the remote.";
            }
            else if (aheadCount > 0)
            {
                summary = $"Local branch is {aheadCount} commit(s) ahead of origin";
                detail = "No remote update is waiting. Review your local branch before pulling anything new.";
            }
            else if (worktreeDirty)
            {
                summary = "No remote updates found, but local edits are present";
                detail = "Install Update stays disabled until the working tree is clean.";
            }
            else
            {
                summary = fetchRemote ? "Branch is synced with origin" : "Local branch looks current";
                detail = fetchRemote
                    ? "MedicAI matches the tracked remote branch."
                    : "Run Check Remote to verify against the latest origin state.";
            }

            return new UpdateSnapshot
            {
                IsRepositoryAvailable = true,
                Branch = branch,
                Commit = commit,
                WorktreeDirty = worktreeDirty,
                AheadCount = aheadCount,
                BehindCount = behindCount,
                Summary = summary,
                Detail = detail,
                RepoPath = repoPath
            };
        }

        private async Task<ProcessResult> RunProcessAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken,
            int timeoutMs = 30000)
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeoutMs);

            try
            {
                using Process process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        WorkingDirectory = workingDirectory,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
                process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                process.StartInfo.StandardErrorEncoding = Encoding.UTF8;

                process.Start();

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync(timeoutCts.Token);

                return new ProcessResult
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = await outputTask,
                    StandardError = await errorTask
                };
            }
            catch (OperationCanceledException)
            {
                return new ProcessResult
                {
                    ExitCode = -1,
                    StandardError = $"Timed out while running: {fileName} {arguments}"
                };
            }
            catch (Exception ex)
            {
                return new ProcessResult
                {
                    ExitCode = -1,
                    StandardError = ex.Message
                };
            }
        }

        private void RelaunchFromStartScript()
        {
            if (string.IsNullOrWhiteSpace(_repositoryRoot))
            {
                AppendActivity("[ERR] Could not locate start.bat for relaunch.", true);
                return;
            }

            string startScript = Path.Combine(_repositoryRoot, "start.bat");
            if (!File.Exists(startScript))
            {
                AppendActivity("[ERR] start.bat is missing, so relaunch has to be manual.", true);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c timeout /t 2 /nobreak > nul && call \"{startScript}\"",
                WorkingDirectory = _repositoryRoot,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            Close();
        }

        private static string FirstMeaningfulLine(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return "No additional details were returned.";
            }

            string[] lines = rawText
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            return lines.Length == 0 ? "No additional details were returned." : lines[0];
        }

        private static string? FindRepositoryRoot()
        {
            string[] probePaths =
            {
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory()
            };

            foreach (string probePath in probePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                DirectoryInfo? current = new DirectoryInfo(probePath);

                while (current != null)
                {
                    string gitPath = Path.Combine(current.FullName, ".git");
                    string solutionPath = Path.Combine(current.FullName, "MedicAI.sln");

                    if (Directory.Exists(gitPath) || File.Exists(gitPath) || File.Exists(solutionPath))
                    {
                        return current.FullName;
                    }

                    current = current.Parent;
                }
            }

            return null;
        }

        private void AppendActivity(string text, bool isError = false)
        {
            Dispatcher.Invoke(() =>
            {
                bool suppressDisconnectLogs = _isInitialized &&
                                              DisconnectKickLoggingToggle.IsChecked != true &&
                                              (text.Contains("disconnect", StringComparison.OrdinalIgnoreCase) ||
                                               text.Contains("kick", StringComparison.OrdinalIgnoreCase));

                if (suppressDisconnectLogs)
                {
                    return;
                }

                string displayText = _isInitialized && TimestampsInLogToggle.IsChecked == true
                    ? $"[{DateTime.Now:HH:mm:ss}] {text}"
                    : text;

                Brush color = isError || text.StartsWith("[ERR]", StringComparison.OrdinalIgnoreCase)
                    ? Brushes.IndianRed
                    : text.StartsWith("[OK]", StringComparison.OrdinalIgnoreCase)
                        ? Brushes.LightGreen
                        : text.StartsWith("[UPD]", StringComparison.OrdinalIgnoreCase)
                            ? Brushes.Gold
                            : text.StartsWith("[SYNC]", StringComparison.OrdinalIgnoreCase)
                                ? Brushes.LightSkyBlue
                                : text.StartsWith("[WARN]", StringComparison.OrdinalIgnoreCase)
                                    ? Brushes.Orange
                                    : Brushes.Gainsboro;

                Paragraph paragraph = new Paragraph(new Run(displayText))
                {
                    Margin = new Thickness(0, 2, 0, 2),
                    Foreground = color
                };

                ActivityLog.Document.Blocks.Add(paragraph);
                ActivityLog.ScrollToEnd();
            });
        }

        private void NavTab_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not RadioButton radioButton)
            {
                return;
            }

            DashboardScroller.Visibility = Visibility.Collapsed;
            PriorityScroller.Visibility = Visibility.Collapsed;
            SettingsScroller.Visibility = Visibility.Collapsed;

            switch (radioButton.Name)
            {
                case "DashboardTabBtn":
                    DashboardScroller.Visibility = Visibility.Visible;
                    break;
                case "PriorityTabBtn":
                    PriorityScroller.Visibility = Visibility.Visible;
                    break;
                case "SettingsTabBtn":
                    SettingsScroller.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _reconnectTimer?.Stop();
            _sessionTimer?.Stop();
            TryAutoExportSessionLog();
            SaveOperatorState();
            _ = DisconnectBot();
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void WindowSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (TryBeginWindowDrag(e))
            {
                e.Handled = true;
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (TryBeginWindowDrag(e))
            {
                e.Handled = true;
            }
        }

        private bool TryBeginWindowDrag(MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || e.ClickCount > 1)
            {
                return false;
            }

            if (IsInteractiveElement(e.OriginalSource as DependencyObject))
            {
                return false;
            }

            try
            {
                DragMove();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool IsInteractiveElement(DependencyObject? source)
        {
            DependencyObject? current = source;

            while (current != null)
            {
                if (current is ButtonBase ||
                    current is TextBoxBase ||
                    current is PasswordBox ||
                    current is Selector ||
                    current is RangeBase ||
                    current is ScrollBar ||
                    current is ScrollViewer ||
                    current is Thumb ||
                    current is Hyperlink)
                {
                    return true;
                }

                current = GetParentObject(current);
            }

            return false;
        }

        private static DependencyObject? GetParentObject(DependencyObject current)
        {
            if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
            {
                return VisualTreeHelper.GetParent(current);
            }

            if (current is FrameworkContentElement frameworkContentElement)
            {
                return frameworkContentElement.Parent;
            }

            if (current is ContentElement contentElement)
            {
                return ContentOperations.GetParent(contentElement);
            }

            return null;
        }
    }
}
