using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using Newtonsoft.Json;

namespace MedicAIGUI.Services
{
    public class MedicBotService
    {
        private static MedicBotService? _instance;
        public static MedicBotService Instance => _instance ??= new MedicBotService();

        private readonly HttpClient _http = new HttpClient();
        private readonly DispatcherTimer _pollTimer;
        private string _botBaseUrl = "http://127.0.0.1:5000";

        public event Action<TelemetrySnapshot>? StatusUpdated;
        public event Action<string, string>? LogReceived;
        public event Action<bool>? ConnectionChanged;

        public TelemetrySnapshot LastTelemetry { get; private set; } = new TelemetrySnapshot();
        public bool IsConnected { get; private set; }
        public bool SimulationMode { get; set; }
        public SavedSettings Settings { get; private set; } = new SavedSettings();

        private MedicBotService()
        {
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _pollTimer.Tick += async (_, __) => await PollStatusAsync();
            _pollTimer.Start();
            LoadLocalSettings();
        }

        public void LoadLocalSettings()
        {
            Settings = SavedSettings.LoadSettings();
            ApplyConnectionSettings(Settings);
        }

        public void ApplyConnectionSettings(SavedSettings settings)
        {
            Settings = settings;
            SetBaseUrl(settings.BuildBaseUrl());
            _http.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.RequestTimeoutSeconds));
        }

        public void SetBaseUrl(string url)
        {
            _botBaseUrl = url.TrimEnd('/');
        }

        public async Task<bool> RefreshStatusAsync()
        {
            await PollStatusAsync();
            return IsConnected;
        }

        private async Task PollStatusAsync()
        {
            if (SimulationMode)
            {
                var rng = new Random();
                var dummyData = new TelemetrySnapshot
                {
                    IsRunning = true,
                    HealingOutput = rng.Next(40, 150),
                    MeleeKills = rng.Next(0, 8),
                    Latency = rng.Next(10, 40),
                    TargetHPGraph = new List<double> { 80, 90, 85, 95, 100, 105, 110, 120, 130, 140, 145, 150 }
                };

                LastTelemetry = dummyData;
                StatusUpdated?.Invoke(dummyData);

                if (!IsConnected)
                {
                    IsConnected = true;
                    ConnectionChanged?.Invoke(true);
                    LogReceived?.Invoke("Info", "Simulation mode active.");
                }

                return;
            }

            try
            {
                using var response = await _http.GetAsync($"{_botBaseUrl}/status");
                if (!response.IsSuccessStatusCode)
                {
                    MarkOffline();
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<TelemetrySnapshot>(json);
                if (data == null)
                {
                    MarkOffline();
                    return;
                }

                LastTelemetry = data;
                StatusUpdated?.Invoke(data);

                if (!IsConnected)
                {
                    IsConnected = true;
                    ConnectionChanged?.Invoke(true);
                    LogReceived?.Invoke("Info", $"Backend connected at {_botBaseUrl}");
                }
            }
            catch
            {
                MarkOffline();
            }
        }

        private void MarkOffline()
        {
            if (!IsConnected)
            {
                return;
            }

            IsConnected = false;
            ConnectionChanged?.Invoke(false);
            LogReceived?.Invoke("Error", "Backend offline.");
        }

        public async Task<bool> SyncConfigAsync(object? config = null)
        {
            var targetConfig = config ?? Settings;

            try
            {
                var json = JsonConvert.SerializeObject(targetConfig);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync($"{_botBaseUrl}/config", content);
                var ok = response.IsSuccessStatusCode;
                LogReceived?.Invoke(ok ? "Info" : "Error", ok ? "Config synced." : $"Config sync failed ({(int)response.StatusCode}).");
                return ok;
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke("Error", $"Config sync error: {ex.Message}");
                return false;
            }
        }

        public Task<bool> SyncBrainConfigAsync()
        {
            return SyncConfigAsync(Settings);
        }

        public async Task<List<string>> GetWeaponsAsync()
        {
            try
            {
                using var response = await _http.GetAsync($"{_botBaseUrl}/weapons");
                if (!response.IsSuccessStatusCode)
                {
                    return new List<string>();
                }

                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>();
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke("Error", $"Failed to fetch weapons: {ex.Message}");
                return new List<string>();
            }
        }

        public async Task<bool> EquipWeaponAsync(string weaponName)
        {
            try
            {
                var json = JsonConvert.SerializeObject(new { weapon = weaponName });
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync($"{_botBaseUrl}/equip_weapon", content);
                var ok = response.IsSuccessStatusCode;
                LogReceived?.Invoke(ok ? "OK" : "Error", ok ? $"Equip request sent for {weaponName}." : $"Equip failed ({(int)response.StatusCode}).");
                return ok;
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke("Error", $"Equip error: {ex.Message}");
                return false;
            }
        }

        public Task<bool> DetectTeamAsync()
        {
            return SendCommandAsync("detect_team", "Detect team");
        }

        public Task<bool> DebugSnapshotAsync()
        {
            return SendCommandAsync("debug_snapshot", "Debug snapshot");
        }

        public async Task<bool> SendCommandAsync(string endpoint, string? label = null)
        {
            var displayLabel = label ?? endpoint.Replace('_', ' ');

            if (SimulationMode)
            {
                await Task.Delay(100);
                LogReceived?.Invoke("OK", $"[simulation] {displayLabel}");
                return true;
            }

            if (!IsConnected)
            {
                LogReceived?.Invoke("Warn", $"{displayLabel}: backend offline.");
                return false;
            }

            try
            {
                using var response = await _http.PostAsync($"{_botBaseUrl}/{endpoint}", null);
                if (response.IsSuccessStatusCode)
                {
                    LogReceived?.Invoke("OK", $"{displayLabel} completed.");
                    return true;
                }

                LogReceived?.Invoke("Error", $"{displayLabel} failed ({(int)response.StatusCode}).");
                return false;
            }
            catch (TaskCanceledException)
            {
                LogReceived?.Invoke("Error", $"{displayLabel} timed out.");
                return false;
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke("Error", $"{displayLabel} error: {ex.Message}");
                return false;
            }
        }
    }
}
