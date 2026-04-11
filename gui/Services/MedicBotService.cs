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

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        private string _botBaseUrl = "http://localhost:5000";
        private readonly DispatcherTimer _pollTimer;

        // ── Events ────────────────────────────────────────────────────────────
        public event Action<TelemetrySnapshot>? StatusUpdated;
        public event Action<string, string>?    LogReceived;
        public event Action<bool>?              ConnectionChanged;

        // ── State ─────────────────────────────────────────────────────────────
        public TelemetrySnapshot LastTelemetry { get; private set; } = new TelemetrySnapshot();
        public bool IsConnected { get; private set; } = false;
        public bool SimulationMode { get; set; } = false;
        public SavedSettings Settings { get; private set; } = new SavedSettings();

        private MedicBotService()
        {
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _pollTimer.Tick += async (s, e) => await PollStatusAsync();
            _pollTimer.Start();
            LoadLocalSettings();
        }

        public void LoadLocalSettings() => Settings = SavedSettings.LoadSettings();

        /// <summary>Change the backend URL at runtime (from Settings view).</summary>
        public void SetBaseUrl(string url) => _botBaseUrl = url.TrimEnd('/');

        // ── Polling ───────────────────────────────────────────────────────────
        private async Task PollStatusAsync()
        {
            if (SimulationMode)
            {
                var dummyData = new TelemetrySnapshot
                {
                    IsRunning = true,
                    HealingOutput = new Random().Next(40, 150),
                    MeleeKills = new Random().Next(5, 20),
                    Latency = new Random().Next(10, 40),
                    TargetHPGraph = new List<double> { 80, 90, 85, 95, 100, 105, 110, 120, 130, 140, 145, 150 }
                };
                
                LastTelemetry = dummyData;
                StatusUpdated?.Invoke(dummyData);

                if (!IsConnected)
                {
                    IsConnected = true;
                    ConnectionChanged?.Invoke(true);
                    LogReceived?.Invoke("Info", $"DEBUG SIMULATION MODE ACTIVE");
                }
                return;
            }

            try
            {
                var res = await _http.GetAsync($"{_botBaseUrl}/status");
                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadAsStringAsync();
                    var data = JsonConvert.DeserializeObject<TelemetrySnapshot>(json);
                    if (data != null)
                    {
                        LastTelemetry = data;
                        StatusUpdated?.Invoke(data);

                        if (!IsConnected)
                        {
                            IsConnected = true;
                            ConnectionChanged?.Invoke(true);
                            LogReceived?.Invoke("Info", $"Backend connected @ {_botBaseUrl}");
                        }
                    }
                }
                else
                {
                    MarkOffline();
                }
            }
            catch
            {
                MarkOffline();
            }
        }

        private void MarkOffline()
        {
            if (IsConnected)
            {
                IsConnected = false;
                ConnectionChanged?.Invoke(false);
                LogReceived?.Invoke("Error", $"Backend offline — start bot_server.py");
            }
        }

        // ── Config Sync ───────────────────────────────────────────────────────
        public async Task<bool> SyncConfigAsync(object? config = null)
        {
            var targetConfig = config ?? Settings;
            try
            {
                var json    = JsonConvert.SerializeObject(targetConfig);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var res     = await _http.PostAsync($"{_botBaseUrl}/config", content);
                var ok      = res.IsSuccessStatusCode;
                LogReceived?.Invoke(ok ? "Info" : "Error", ok ? "Config synced to node." : $"Config sync failed ({(int)res.StatusCode})");
                return ok;
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke("Error", $"Config sync error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SyncBrainConfigAsync()
        {
            return await SyncConfigAsync(Settings);
        }

        // ── New Features ──────────────────────────────────────────────────────
        public async Task<List<string>> GetWeaponsAsync()
        {
            try
            {
                var res = await _http.GetAsync($"{_botBaseUrl}/weapons");
                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>();
                }
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke("Error", $"Failed to fetch weapons: {ex.Message}");
            }
            return new List<string>();
        }

        public async Task<bool> EquipWeaponAsync(string weaponName)
        {
            try
            {
                var json = JsonConvert.SerializeObject(new { weapon = weaponName });
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var res = await _http.PostAsync($"{_botBaseUrl}/equip_weapon", content);
                bool ok = res.IsSuccessStatusCode;
                LogReceived?.Invoke(ok ? "OK" : "Error", ok ? $"Equip command for {weaponName} sent." : $"Equip failed ({(int)res.StatusCode})");
                return ok;
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke("Error", $"Equip error: {ex.Message}");
                return false;
            }
        }

        public async Task DetectTeamAsync() => await SendCommandAsync("detect_team", "DETECT TEAM");
        
        public async Task DebugSnapshotAsync() => await SendCommandAsync("debug_snapshot", "DEBUG SNAPSHOT");

        // ── Command Dispatch ──────────────────────────────────────────────────
        /// <summary>
        /// Sends a POST command to the bot backend and logs the result.
        /// Returns true on HTTP 2xx, false otherwise.
        /// </summary>
        public async Task<bool> SendCommandAsync(string endpoint, string? label = null)
        {
            var displayLabel = label ?? endpoint.Replace('_', ' ').ToUpper();

            if (SimulationMode)
            {
                await Task.Delay(200);
                LogReceived?.Invoke("OK", $"[DEBUG MOCK] [{displayLabel}] executed successfully.");
                return true;
            }

            if (!IsConnected)
            {
                LogReceived?.Invoke("Warn", $"[{displayLabel}] Backend offline — command not sent.");
                return false;
            }

            try
            {
                var res = await _http.PostAsync($"{_botBaseUrl}/{endpoint}", null);
                if (res.IsSuccessStatusCode)
                {
                    LogReceived?.Invoke("OK", $"[{displayLabel}] executed successfully.");
                    return true;
                }
                else
                {
                    LogReceived?.Invoke("Error", $"[{displayLabel}] server returned {(int)res.StatusCode}.");
                    return false;
                }
            }
            catch (TaskCanceledException)
            {
                LogReceived?.Invoke("Error", $"[{displayLabel}] timed out — server may be busy.");
                return false;
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke("Error", $"[{displayLabel}] failed: {ex.Message}");
                return false;
            }
        }
    }
}
