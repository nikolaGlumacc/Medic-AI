using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.NetworkInformation;
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
        private readonly string _configPath = FindConfigPath();

        private static string FindConfigPath()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 8; i++)
            {
                if (dir == null) break;
                var candidate = Path.Combine(dir.FullName, "bot", "bot_config.json");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bot_config.json");
        }

        public SavedSettings Settings { get; private set; } = new SavedSettings();

        public event Action<JObject>? StatusUpdated;
        public event Action<bool>? ConnectionChanged;
        public event Action<string>? OnActivity;
        public event Action<string>? LogReceived;
        public event Action<long>? OnPong;
        public event Action<int>? OnConfigSyncAck;
        public event Action<string>? OnVaccinatorResistChanged;

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
            _wsUrl = $"ws://{settings.BotIp}:{settings.WsPort}";
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

        public async Task<string> CheckHealthAsync()
        {
            var sb = new StringBuilder();
            sb.AppendLine("--- CONNECTION HEALTH REPORT ---");
            
            string host = Settings.BotIp;
            int port = Settings.FlaskPort;
            
            // 1. PING
            sb.Append("[1/3] PING Test: ");
            try
            {
                var ping = new Ping();
                var reply = await ping.SendPingAsync(host, 2000);
                if (reply.Status == IPStatus.Success)
                    sb.AppendLine($"SUCCESS ({reply.RoundtripTime}ms)");
                else
                    sb.AppendLine($"FAILED ({reply.Status})");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"ERROR ({ex.Message})");
            }

            // 2. HTTP HANDSHAKE
            sb.Append("[2/3] HTTP Handshake (port 5000): ");
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var response = await client.GetAsync($"http://{host}:{port}/status");
                if (response.IsSuccessStatusCode)
                    sb.AppendLine("SUCCESS (Server is responsive)");
                else
                    sb.AppendLine($"FAILED (Status: {response.StatusCode})");
            }
            catch (HttpRequestException)
            {
                sb.AppendLine("FAILED (Connection Refused)");
                sb.AppendLine("    ↳ Probable Cause: Server not running or Firewall blocking port 5000.");
            }
            catch (TaskCanceledException)
            {
                sb.AppendLine("TIMEOUT");
                sb.AppendLine("    ↳ Probable Cause: Network routing issue or aggressive Firewall.");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"ERROR ({ex.Message})");
            }

            // 3. WEBSOCKET PROBE
            sb.Append($"[3/3] WebSocket Probe (port {Settings.WsPort}): ");
            try
            {
                using var probeWs = new ClientWebSocket();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await probeWs.ConnectAsync(new Uri(_wsUrl), cts.Token);
                sb.AppendLine("SUCCESS (Handshake OK)");
                await probeWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "Probe done", CancellationToken.None);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"FAILED ({ex.Message})");
                sb.AppendLine($"    ↳ Probable Cause: WebSocket service not listening on port {Settings.WsPort}.");
            }

            sb.AppendLine("\nFINAL VERDICT:");
            bool httpOk = sb.ToString().Contains("SUCCESS (Server is responsive)");
            bool wsOk = sb.ToString().Contains("SUCCESS (Handshake OK)");

            if (httpOk && wsOk)
                sb.AppendLine("✅ SYSTEM SHOULD BE WORKING. (Ping failures are normal due to Windows Firewall rules)");
            else
            {
                sb.AppendLine("❌ CONNECTION ISSUES DETECTED.");
                sb.AppendLine("\n--- LAPTOP & CROSS-DEVICE TIPS ---");
                sb.AppendLine("1. Ensure machine running the bot is on 'Private' network (not 'Public').");
                sb.AppendLine("2. Run build.bat as Administrator to auto-configure Firewall.");
                sb.AppendLine("3. Ensure target laptop's IP address matches 'Bot Account IP' in Settings.");
                sb.AppendLine("4. Disable 'Aggressive Power Saving' in Windows Battery settings.");
            }

            return sb.ToString();
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
                    else if (type == "pong")
                    {
                        if (long.TryParse(message["ts"]?.ToString(), out long ts)) OnPong?.Invoke(ts);
                    }
                    else if (type == "config_sync_ack")
                    {
                        if (int.TryParse(message["value"]?.ToString(), out int val)) OnConfigSyncAck?.Invoke(val);
                    }
                    else if (type == "vaccinator_resist")
                    {
                        var resist = message["resist"]?.ToString();
                        if (resist != null) OnVaccinatorResistChanged?.Invoke(resist);
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

        public async Task SendCycleVaccinatorAsync() => await SendAsync(JsonSerializer.Serialize(new { type = "cycle_vaccinator" }));
        public async Task SendPopUberAsync() => await SendAsync(JsonSerializer.Serialize(new { type = "pop_uber" }));

        public async Task SendPingAsync(long ts) => await SendAsync(JsonSerializer.Serialize(new { type = "ping", ts }));
        public async Task SendConfigSyncTestAsync(int value) => await SendAsync(JsonSerializer.Serialize(new { type = "config_sync_test", value }));
        public async Task SendHardwareDanceAsync() => await SendAsync(JsonSerializer.Serialize(new { type = "hardware_dance" }));

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