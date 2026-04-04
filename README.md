# MedicAI - TF2 Medic Bot with Dashboard

A sophisticated Team Fortress 2 Medic bot automation system with real-time WebSocket communication and a professional WPF GUI dashboard.

## Features

### 🤖 Bot Server
- **WebSocket Server** - Real-time communication with GUI dashboard on port 8765
- **Automatic Medic Gameplay** - Intelligent following, healing, and Uber management
- **Computer Vision Integration** - YOLOv8-based environment awareness
- **Configurable Behavior** - Adjustable follow modes, Uber strategies, weapon loadouts
- **Session Tracking** - Real-time statistics (healing, kills, session duration)
- **Priority List Management** - Define and track priority targets
- **Whitelist/Blacklist** - Control which players to support or avoid
- **Smart Spy Detection** - Monitors and alerts on potential spies
- **CPU Thermal Management** - Auto-throttles if system overheats

### 💻 GUI Dashboard
- **Dark Modern Interface** - Premium glassmorphism design
- **Live Bot Status** - Real-time connection state, mode, uber charge
- **Control Center** - Deploy bot, connect/disconnect, manage settings
- **Weapon Configuration** - Select primary, secondary, and melee weapons
- **Player Management** - Add/remove priority players with tier system
- **Activity Feed** - Real-time logging of bot actions
- **Respawn Timers** - Visual progress indicators for respawning teammates
- **Session Statistics** - Melee kills, total healing, session duration
- **Settings Panel** - Adjust follow distance, spy check frequency, volume, and more

## Quick Start

### Prerequisites
- **Windows 10/11**
- **Python 3.11+** (installed in system)
- **.NET 10 Runtime** (or later)
- **Team Fortress 2** (running)

### Installation

1. **Clone the repository:**
   ```bash
   git clone https://github.com/nikolaGlumacc/Medic-AI.git
   cd Medic-AI
   ```

2. **Install Python dependencies:**
   ```bash
   python -m venv c:\medicai_venv
   c:\medicai_venv\Scripts\pip install -r requirements.txt
   ```

3. **Build the GUI (Optional - pre-built executable included):**
   ```bash
   cd gui
   dotnet build
   cd ..
   ```

4. **Run the application:**
   ```bash
   start.bat
   ```

Two windows will open:
- **Bot Server Window** - Shows "Bot WebSocket server running on port 8765"
- **MedicAI Dashboard** - Professional GUI interface

## Configuration

Edit `bot/bot_server.py` to modify:
- Server port (default: 8765)
- Follow distance thresholds
- Spy check frequency
- Uber behavior modes
- Vision engine parameters

## File Structure

```
MedicAI/
├── bot/
│   ├── bot_server.py          # Main WebSocket server and bot logic
│   ├── vision_engine.py        # YOLOv8 computer vision integration
│   └── watchdog.py             # Process monitoring
├── gui/
│   ├── MainWindow.xaml         # WPF UI layout (dark theme)
│   ├── MainWindow.xaml.cs      # C# code-behind and event handlers
│   ├── App.xaml                # Application resources
│   └── MedicAIGUI.csproj       # .NET project file
├── start.bat                   # Launcher script
├── requirements.txt            # Python dependencies
├── MedicAI.sln                 # Visual Studio solution
└── README.md                   # This file
```

## Technology Stack

### Backend
- **Python 3.11** - Bot logic and server
- **websockets 16.0** - Real-time WebSocket communication
- **pynput** - Keyboard and mouse input control
- **psutil** - System monitoring (CPU temp)
- **YOLOv8** - Computer vision (optional)

### Frontend
- **C# WPF** - Desktop GUI framework
- **.NET 10** - Framework version
- **XAML** - UI markup

## Bot Behavior Modes

### Active Mode
- Actively follows priority player
- Maintains healing beam
- Auto-attacks enemies with melee if alone
- Responds to "come with me" commands

### Passive Mode
- Defensive positioning
- Responds to multiple medic calls with active switch
- Listens for teammate calls

## Connection Flow

```
1. User runs start.bat
2. Bot server starts on localhost:8765
3. GUI dashboard launches
4. GUI connects to WebSocket server
5. Configuration sent to bot
6. Bot awaits "start" command
7. Bot begins gameplay automation
```

## API - WebSocket Messages

### GUI → Bot

**Configuration:**
```json
{
  "type": "config",
  "config": {
    "primary_weapon": "Crusader's Crossbow",
    "follow_distance": 50,
    "uber_behavior": "Manual"
  }
}
```

**Start Bot:**
```json
{
  "type": "start"
}
```

**Set Follow Mode:**
```json
{
  "type": "set_follow_mode",
  "mode": "active"
}
```

### Bot → GUI

**Activity:**
```json
{
  "type": "activity",
  "msg": "Uber ready!",
  "audio": true,
  "audio_file": "uber_ready"
}
```

## Troubleshooting

### Bot Server Won't Start
- Ensure Python 3.11+ is installed
- Verify virtual environment exists: `c:\medicai_venv\`
- Check port 8765 is not already in use: `netstat -ano | findstr :8765`

### GUI Won't Connect
- Ensure bot server window shows "running on port 8765"
- Try restarting both applications
- Check firewall settings

### Deprecation Warnings
- All updated to modern websockets API (v16.0+)
- Using `websockets.serve()` instead of deprecated `websockets.server.serve()`

## Development

### Building the GUI
```bash
cd gui
dotnet restore
dotnet build
```

### Running Tests
```bash
cd bot
python -m pytest
```

### Code Structure

**bot_server.py** - 310 lines
- `MedicAIBot` class: Main bot logic and state management
- Event handlers for WebSocket messages
- Bot main loop with gameplay automation
- Utility methods for movement, combat, item management

**MainWindow.xaml.cs** - C# event handlers
- WebSocket connection management
- UI update coordination
- Configuration push to bot
- Activity logging

## Future Roadmap

- [ ] Advanced team composition awareness
- [ ] Map-specific strategies (2Fort, Payload, etc.)
- [ ] Real-time update streaming
- [ ] Replay analysis system
- [ ] Cross-class bot support
- [ ] Machine learning model for optimal behavior
- [ ] Discord integration for notifications
- [ ] Database for session analytics

## License

This project is provided as-is for educational purposes.

## Credits

Built with:
- Python WebSockets
- C# WPF
- YOLOv8 by Ultralytics
- pynput library

## Support

For issues or feature requests, open a GitHub issue or check the [QUICK_START.md](QUICK_START.md) guide.

---

**Status:** ✅ Fully Functional | **Last Updated:** April 4, 2026
