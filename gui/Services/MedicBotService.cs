using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MedicAIGUI.Services
{
    public class MedicBotService
    {
        private static MedicBotService? _instance;
        public static MedicBotService Instance => _instance ??= new MedicBotService();

        private ClientWebSocket? _ws;
        private string _wsUrl = "ws://localhost:8766";
        private readonly string _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "bot", "bot_config.json");

        public SavedSettings Settings { get; private set; } = new SavedSettings();

        public event Action<JObject>? StatusUpdated;
        public event Action<bool>? ConnectionChanged;
        public event Action<string>? OnActivity;
        public event Action<string>? LogReceived;

        public bool IsConnected => _ws?.State == WebSocketState.Open;
        public bool IsSimulationMode { get; private set; }
        public JObject? LastTelemetry { get; private set; }

        private System.Timers.Timer? _simTimer;
        private Random _rng = new();
        private double _fakeUber = 0;
        private bool _isSimRunning = false;

        private MedicBotService() { }

        public void LoadLocalSettings()
        {
            Settings = SavedSettings.Load();
            ApplyConnectionSettings(Settings);
        }

        public void ApplyConnectionSettings(SavedSettings settings)
        {
            _wsUrl = $"ws://{settings.BotIp}:{settings.BotPort}";
            DebugHub.Log($"SERVICE: WS Endpoint updated to {_wsUrl}");
        }

        public void ToggleSimulation()
        {
            IsSimulationMode = !IsSimulationMode;
            if (IsSimulationMode)
            {
                _simTimer?.Stop();
                _simTimer = new System.Timers.Timer(500);
                _simTimer.Elapsed += (s, e) => ProduceFakeTelemetry();
                _simTimer.Start();
                DebugHub.Log("SERVICE: Simulation Mode STARTED");
                ConnectionChanged?.Invoke(true);
            }
            else
            {
                _simTimer?.Stop();
                DebugHub.Log("SERVICE: Simulation Mode STOPPED");
                ConnectionChanged?.Invoke(IsConnected);
            }
        }

        private void ProduceFakeTelemetry()
        {
            _fakeUber = (_fakeUber + _rng.NextDouble() * 2) % 100;
            var telemetry = new JObject
            {
                ["type"] = "status",
                ["running"] = _isSimRunning,
                ["uber"] = _fakeUber,
                ["healing"] = _rng.Next(10, 80),
                ["melee_kills"] = _rng.Next(0, 50),
                ["session_seconds"] = (int)(DateTime.Now - DateTime.Today).TotalSeconds % 3600,
                ["latency"] = _rng.Next(5, 45)
            };

            LastTelemetry = telemetry;
            StatusUpdated?.Invoke(telemetry);

            if (_rng.NextDouble() > 0.8)
            {
                var msg = "SIMULATION: Bot logic pulse...";
                OnActivity?.Invoke(msg);
                LogReceived?.Invoke(msg);
            }
        }

        public async Task ConnectAsync()
        {
            if (IsConnected) return;

            DebugHub.Log($"SERVICE: Connecting to {_wsUrl}...");
            try
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();
                
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _ws.ConnectAsync(new Uri(_wsUrl), cts.Token);
                
                ConnectionChanged?.Invoke(true);
                DebugHub.Log("SERVICE: Connection established ✔");
                _ = Task.Run(ReceiveLoop);
            }
            catch (Exception ex)
            {
                DebugHub.Log($"SERVICE: Connection failed ✘ ({ex.Message})");
                ConnectionChanged?.Invoke(false);
            }
        }

        public void Disconnect()
        {
            try
            {
                if (_ws?.State == WebSocketState.Open || _ws?.State == WebSocketState.CloseReceived)
                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by user", CancellationToken.None);
            }
            catch { }
            ConnectionChanged?.Invoke(false);
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[4096];
            try
            {
                while (_ws?.State == WebSocketState.Open)
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var message = JObject.Parse(json);
                    var type = message["type"]?.ToString();
                    if (type == "status")
                    {
                        LastTelemetry = message;
                        StatusUpdated?.Invoke(message);
                    }
                    else if (type == "activity")
                    {
                        var msg = message["msg"]?.ToString() ?? "";
                        OnActivity?.Invoke(msg);
                        LogReceived?.Invoke(msg);
                    }
                    else if (type == "terminal_log")
                    {
                        var line = message["line"]?.ToString() ?? "";
                        LogReceived?.Invoke($"[BOT] {line}");
                    }
                }
            }
            catch { }
        }

        public async Task SendCommand(string command)
        {
            if (IsSimulationMode)
            {
                DebugHub.Log($"SIMULATION: Handled command '{command}'");
                if (command == "start") _isSimRunning = true;
                if (command == "stop") _isSimRunning = false;
                if (command == "test_input")
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1000);
                        OnActivity?.Invoke("TEST_INPUT_SUCCESS");
                        LogReceived?.Invoke("SIMULATION: Input Driver Confirmation received.");
                    });
                }
                return;
            }

            var msg = new { type = command };
            await SendAsync(JsonSerializer.Serialize(msg));
        }

        public async Task SendConfigUpdate(Dictionary<string, object> config)
        {
            var msg = new { type = "config", config };
            await SendAsync(JsonSerializer.Serialize(msg));
        }

        public async Task EquipWeapon(string weaponName)
        {
            var msg = new { type = "equip_weapon", weapon = weaponName };
            await SendAsync(JsonSerializer.Serialize(msg));
        }

        public async Task<List<string>> GetAvailableWeapons()
        {
            return new List<string> { "crusaders_crossbow", "medi_gun", "ubersaw" };
        }

        public JObject? GetConfigFromFile()
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                return JObject.Parse(json);
            }
            catch { return null; }
        }

        public async Task<bool> SyncConfigAsync()
        {
            try
            {
                // CRITICAL FIX: Send the LIVE Settings object, not the stale file on disk
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(Settings, options);
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                
                if (dict != null)
                {
                    await SendConfigUpdate(dict);
                    DebugHub.Log("SERVICE: Live settings synced to Bot.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                DebugHub.Log($"SERVICE: Sync failed: {ex.Message}");
            }
            return false;
        }

        public async Task StartBotAsync() => await SendCommand("start");
        public async Task StopBotAsync() => await SendCommand("stop");
        public async Task RefreshStatusAsync() => await SendCommand("status");
        public async Task DebugSnapshotAsync() => await SendCommand("debug_snapshot");
        public async Task SendTestInputAsync() => await SendCommand("test_input");
        public async Task<List<string>> GetWeaponsAsync() => await GetAvailableWeapons();

        private async Task SendAsync(string message)
        {
            try
            {
                if (_ws?.State == WebSocketState.Open)
                {
                    var bytes = Encoding.UTF8.GetBytes(message);
                    await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            catch { }
        }
    }
}