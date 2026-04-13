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
        private readonly string _wsUrl = "ws://localhost:8766";
        private readonly string _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "bot", "bot_config.json");

        public SavedSettings Settings { get; private set; } = new SavedSettings();

        // Events used by views
        public event Action<JObject>? StatusUpdated;
        public event Action<bool>? ConnectionChanged;
        public event Action<string>? OnActivity;
        public event Action<string>? LogReceived;

        public bool IsConnected => _ws?.State == WebSocketState.Open;
        public JObject? LastTelemetry { get; private set; }

        private MedicBotService() { }

        public void LoadLocalSettings()
        {
            Settings = SavedSettings.Load();
        }

        public void ApplyConnectionSettings(SavedSettings settings) { }

        public async Task ConnectAsync()
        {
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri(_wsUrl), CancellationToken.None);
            ConnectionChanged?.Invoke(true);
            _ = Task.Run(ReceiveLoop);
        }

        public void Disconnect()
        {
            _ws?.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by user", CancellationToken.None);
            ConnectionChanged?.Invoke(false);
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[4096];
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
            }
        }

        public async Task SendCommand(string command)
        {
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
                var config = GetConfigFromFile();
                if (config != null)
                {
                    var dict = config.ToObject<Dictionary<string, object>>();
                    if (dict != null)
                    {
                        await SendConfigUpdate(dict);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public async Task StartBotAsync() => await SendCommand("start");
        public async Task StopBotAsync() => await SendCommand("stop");
        public async Task RefreshStatusAsync() => await SendCommand("status");
        public async Task DebugSnapshotAsync() => await SendCommand("debug_snapshot");
        public async Task<List<string>> GetWeaponsAsync() => await GetAvailableWeapons();

        private async Task SendAsync(string message)
        {
            if (_ws?.State == WebSocketState.Open)
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
    }
}