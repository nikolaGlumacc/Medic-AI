import asyncio
import websockets
import json
import time
import threading
import os
import psutil
from datetime import datetime

try:
    from vision_engine import VisionEngine
except ImportError:
    VisionEngine = None
try:
    from watchdog import WatchdogProcess
except ImportError:
    WatchdogProcess = None

from pynput.keyboard import Controller as KeyboardController, Key
from pynput.mouse import Controller as MouseController, Button

CONFIG_PATH = 'config.json'

class MedicAIBot:
    def __init__(self):
        self.config = {}
        self.session_start = None
        self.running = False
        self.follow_mode = 'active'
        self.ws = None
        self.loop = None
        self.last_scoreboard_check = 0
        
        self.current_target = None
        self.target_health = 100
        self.health = 100
        self.uber_charge = 0.0
        self.uber_ready = False
        self.uber_active = False
        self.uber_start_time = None
        self.enemies_nearby = 0
        self.medic_calls = 0
        
        # State vars
        self.is_falling = False
        self.is_reloading = False
        self.is_jumping = False
        self.is_retreating = False
        self.is_erratic = False
        self.is_stopped = False
        
        self.time_since_visual_lost = 0
        self.time_in_search = 0
        self.stuck_count = 0
        self.stuck_zones = set()
        self.problem_visual_areas = set()
        self.danger_zones = set()
        self.enemy_kill_counts = {}
        
        self.cpu_overheat = False
        self.spy_alertness = 0
        self.respawn_timers = {}
        self.priority_status = {}
        
        self.keyboard = KeyboardController()
        self.mouse = MouseController()
        self.vision = VisionEngine() if VisionEngine else None
        self.watchdog = WatchdogProcess() if WatchdogProcess else None

    async def handle_ws(self, websocket):
        self.ws = websocket
        import websockets.exceptions
        try:
            async for message in websocket:
                data = json.loads(message)
                if data.get('type') == 'config':
                    self.config = data['config']
                elif data.get('type') == 'start':
                    self.session_start = datetime.now()
                    self.running = True
                    threading.Thread(target=self.bot_main_loop, daemon=True).start()
                elif data.get('type') == 'set_follow_mode':
                    self.follow_mode = data.get('mode', 'active')
                    await self.send_activity(f"Follow mode changed to {self.follow_mode}", True, "mode_switch")
        except websockets.exceptions.ConnectionClosed:
            print("WPF Client disconnected gracefully. Awaiting new socket.")
        except Exception as e:
            print(f"WS Exception: {e}")

    async def send_activity(self, msg, audio_trigger=False, audio_file="none"):
        if self.ws:
            payload = {'type': 'activity', 'msg': msg, 'audio': audio_trigger, 'audio_file': audio_file}
            asyncio.run_coroutine_threadsafe(self.ws.send(json.dumps(payload)), self.loop)

    def play_local_sound(self, sound_name):
        pass

    def bot_main_loop(self):
        self.skip_intro()
        self.equip_loadout()
        self.enter_spawn()
        self.pre_overheal_priority()
        self.wait_for_round_start()

        while self.running:
            self.check_cpu_temp()
            if self.cpu_overheat:
                time.sleep(1)
                continue
            
            self.update_environment()

            # Self-preservation overrides everything
            if self.health < 40 and self.target_health == 100 and self.enemies_nearby > 0:
                self.find_health_pack()
                continue
                
            # Combat handling if alone
            if not self.priorities_alive() and self.enemies_nearby > 0:
                self.handle_melee_combat()
                continue
            elif not self.priorities_alive():
                self.return_to_spawn()
                continue

            # Uber logic
            self.handle_uber()

            # Follow logic
            if self.follow_mode == 'active':
                self.active_follow()
            else:
                self.passive_follow()

            self.monitor_kill_feed()
            self.monitor_scoreboard()
            self.check_spies()
            time.sleep(0.05)

    def priorities_alive(self):
        for p in self.config.get('priority_list', []):
            if self.priority_status.get(p['name'], {}).get('state') == 'alive':
                return True
        return False

    def skip_intro(self): pass
    def equip_loadout(self): pass
    def enter_spawn(self): pass
    def pre_overheal_priority(self): pass
    def wait_for_round_start(self): pass

    def update_environment(self):
        if self.vision:
            pass

    def active_follow(self):
        if self.time_since_visual_lost > 3:
            if self.time_since_visual_lost == 3.1:
                asyncio.run_coroutine_threadsafe(self.send_activity("Lost priority player, searching...", True, "lost_target"), self.loop)
            self.time_in_search += 0.05
            self.mouse.move(15, 0)
            if self.time_in_search > 15:
                self.return_to_spawn()
            return
            
        self.time_in_search = 0
        
        if self.is_falling:
            self.mouse.move(0, -50)
            
        if self.is_reloading:
            return
            
        if self.is_stopped:
            return
            
        if self.target_health > 140 and self.enemies_nearby == 0:
            self.keyboard.tap('s')
            
        if self.is_retreating:
            self.keyboard.tap('s')
            
        if self.is_jumping:
            self.keyboard.tap(Key.space)
            self.mouse.move(0, 20)

        if self.check_stuck():
            self.stuck_count += 1
            if self.stuck_count >= 3:
                self.stuck_zones.add("current_pos")
                self.keyboard.tap('a')
        else:
            self.stuck_count = 0
            if self.is_erratic:
                self.keyboard.tap('a')
                self.keyboard.tap('d')
            if "current_pos" in self.problem_visual_areas:
                self.keyboard.press('w')
            else:
                self.keyboard.tap('w')

    def passive_follow(self):
        self.keyboard.release('w')
        if not self.config.get('disable_passive_audio', False):
            pass
            
        if self.medic_calls > 0:
            self.mouse.move(10, 0)
            if self.medic_calls == 1:
                self.keyboard.press('w')
                self.medic_calls = 0
            elif self.medic_calls >= 2:
                self.follow_mode = 'active'
                asyncio.run_coroutine_threadsafe(self.send_activity("Come with me! Switching to active.", True, "mode_switch"), self.loop)
                self.medic_calls = 0
        else:
            self.mouse.move(2, 0)
            
        if self.enemies_nearby > 0:
            self.keyboard.tap('s')

    def find_health_pack(self):
        self.mouse.move(10,0)
        self.keyboard.tap('w')

    def handle_melee_combat(self):
        self.keyboard.tap('3')
        if self.enemies_nearby == 1:
            self.keyboard.press('w')
            self.mouse.click(Button.left)
            asyncio.run_coroutine_threadsafe(self.send_activity("Melee kill!", True, "medic_kill"), self.loop)
        elif self.enemies_nearby > 1 or self.health < 20:
            self.mouse.move(180, 0)
            self.keyboard.press('w')
            self.find_health_pack()

    def handle_uber(self):
        if self.uber_active:
            if time.time() - self.uber_start_time > 8:
                self.uber_active = False
                asyncio.run_coroutine_threadsafe(self.send_activity("Uber ended. Wasted: False. Duration: 8s.", True, "uber_cooldown"), self.loop)
            return

        if self.uber_charge >= 100 and not self.uber_ready:
            self.uber_ready = True
            asyncio.run_coroutine_threadsafe(self.send_activity("Uber ready!", True, "uber_ready"), self.loop)
            
        ub_behavior = self.config.get('uber_behavior', 'Manual')
        if ub_behavior == 'Auto-pop' and self.target_health < 30 and self.uber_ready:
            self.pop_uber()
        elif ub_behavior == 'Suggest' and self.enemies_nearby >= 3 and self.uber_ready:
            asyncio.run_coroutine_threadsafe(self.send_activity("Suggesting Uber! (Pushing group)", True, "uber_ready"), self.loop)

    def pop_uber(self):
        self.mouse.click(Button.right)
        self.uber_ready = False
        self.uber_active = True
        self.uber_charge = 0
        self.uber_start_time = time.time()
        asyncio.run_coroutine_threadsafe(self.send_activity("Uber activated!", True, "uber_activated"), self.loop)

    def monitor_kill_feed(self): pass
    
    def monitor_scoreboard(self):
        current_time = time.time()
        if current_time - self.last_scoreboard_check > 30:
            self.last_scoreboard_check = current_time
            self.keyboard.press(Key.tab)
            self.keyboard.release(Key.tab)

    def return_to_spawn(self):
        for name, data in self.respawn_timers.items():
            if data['time'] <= 0:
                self.keyboard.tap('w')
                break

    def check_spies(self):
        if time.time() % self.config.get('spy_check_frequency', 10) < 0.1:
            self.mouse.move(180, 0)
            if self.spy_alertness > 0:
                asyncio.run_coroutine_threadsafe(self.send_activity("Spy detected!", True, "spy"), self.loop)
                self.play_local_sound("spy")
            self.mouse.move(180, 0)
            
        if self.health <= 0:
            asyncio.run_coroutine_threadsafe(self.send_activity("Backstabbed!", True, "spy"), self.loop)
            self.play_local_sound("spy")

    def check_stuck(self): return False
    def check_cpu_temp(self):
        try:
            temps = psutil.sensors_temperatures()
            if 'coretemp' in temps:
                highest = max(t.current for t in temps['coretemp'])
                if highest > 85: self.cpu_overheat = True
                else: self.cpu_overheat = False
        except: pass
    
    def reconnect(self): pass

    async def handle_ws(self, websocket, path):
        """Handle WebSocket connections from GUI"""
        print(f"New WebSocket connection from {websocket.remote_address}")
        self.ws = websocket

        try:
            async for message in websocket:
                try:
                    data = json.loads(message)
                    msg_type = data.get('type', 'unknown')

                    if msg_type == 'ping':
                        await websocket.send(json.dumps({'type': 'pong', 'timestamp': time.time()}))
                    elif msg_type == 'status':
                        status = {
                            'type': 'status',
                            'running': self.running,
                            'health': self.health,
                            'target_health': self.target_health,
                            'uber_charge': self.uber_charge,
                            'uber_ready': self.uber_ready,
                            'enemies_nearby': self.enemies_nearby,
                            'current_target': self.current_target,
                            'session_time': time.time() - (self.session_start or time.time())
                        }
                        await websocket.send(json.dumps(status))
                    elif msg_type == 'start_bot':
                        if not self.running:
                            self.running = True
                            self.session_start = time.time()
                            threading.Thread(target=self.bot_main_loop, daemon=True).start()
                            await websocket.send(json.dumps({'type': 'bot_started'}))
                        else:
                            await websocket.send(json.dumps({'type': 'error', 'message': 'Bot already running'}))
                    elif msg_type == 'stop_bot':
                        self.running = False
                        await websocket.send(json.dumps({'type': 'bot_stopped'}))
                    elif msg_type == 'update_config':
                        self.config.update(data.get('config', {}))
                        await websocket.send(json.dumps({'type': 'config_updated'}))
                    elif msg_type == 'get_config':
                        await websocket.send(json.dumps({'type': 'config', 'config': self.config}))
                    else:
                        await websocket.send(json.dumps({'type': 'error', 'message': f'Unknown message type: {msg_type}'}))

                except json.JSONDecodeError:
                    await websocket.send(json.dumps({'type': 'error', 'message': 'Invalid JSON'}))
                except Exception as e:
                    await websocket.send(json.dumps({'type': 'error', 'message': str(e)}))

        except websockets.exceptions.ConnectionClosed:
            print("WebSocket connection closed")
        finally:
            if self.ws == websocket:
                self.ws = None

    async def send_activity(self, message, important=False, activity_type="info"):
        """Send activity update to GUI"""
        if self.ws:
            try:
                activity_data = {
                    'type': 'activity',
                    'message': message,
                    'important': important,
                    'activity_type': activity_type,
                    'timestamp': time.time()
                }
                await self.ws.send(json.dumps(activity_data))
            except Exception as e:
                print(f"Failed to send activity: {e}")

    def play_local_sound(self, sound_type):
        """Play notification sounds (placeholder)"""
        pass

    async def run_server(self):
        print('Bot WebSocket server running on port 8765')
        async with websockets.serve(self.handle_ws, '0.0.0.0', 8765):
            await asyncio.Future()  # run forever

    def run(self):
        self.loop = asyncio.new_event_loop()
        asyncio.set_event_loop(self.loop)
        self.loop.run_until_complete(self.run_server())

if __name__ == '__main__':
    bot = MedicAIBot()
    bot.run()
