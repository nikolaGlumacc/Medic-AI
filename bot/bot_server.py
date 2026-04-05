import asyncio
import json
import time
import threading
from datetime import datetime

import psutil
import websockets
import websockets.exceptions

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


class MedicAIBot:
    def __init__(self):
        self.config = {}
        self.session_start = None
        self.running = False
        self.follow_mode = 'active'
        self.bot_state = 'standby'
        self.ws = None
        self.loop = None

        self.current_target = None
        self.target_health = 100
        self.health = 100
        self.uber_charge = 0.0
        self.uber_ready = False
        self.uber_active = False
        self.uber_start_time = None

        self.cpu_overheat = False
        self.spy_alertness = 0
        self.heal_button_held = False
        self.warned_about_ocr = False
        self.last_environment_update = 0.0
        self.environment_update_interval = 0.25
        self.last_status_push = 0.0
        self.status_push_interval = 0.5
        self.last_any_heal_target_time = 0.0
        self.last_heal_weapon_select = 0.0

        self.keyboard = KeyboardController()
        self.mouse = MouseController()
        self.vision = VisionEngine() if VisionEngine else None
        self.watchdog = WatchdogProcess() if WatchdogProcess else None

    # ------------------------------------------------------------------ #
    #  Messaging helpers                                                   #
    # ------------------------------------------------------------------ #

    async def send_activity(self, msg, audio_trigger=False, audio_file="none"):
        if self.ws:
            try:
                payload = {
                    'type': 'activity',
                    'msg': msg,
                    'audio': audio_trigger,
                    'audio_file': audio_file
                }
                await self.ws.send(json.dumps(payload))
            except Exception as e:
                print(f"[send_activity] Failed: {e}")

    def _queue_payload(self, payload):
        if self.ws and self.loop:
            try:
                asyncio.run_coroutine_threadsafe(
                    self.ws.send(json.dumps(payload)),
                    self.loop
                )
            except Exception as e:
                print(f"[queue_payload] Failed: {e}")

    def send_activity_sync(self, msg, audio_trigger=False, audio_file="none"):
        payload = {
            'type': 'activity',
            'msg': msg,
            'audio': audio_trigger,
            'audio_file': audio_file
        }
        self._queue_payload(payload)

    def build_status_payload(self):
        return {
            'type': 'status',
            'mode': self.bot_state,
            'uber': int(round(self.uber_charge)),
            'health': self.health,
            'target': self.current_target or "",
            'target_health': self.target_health,
            'running': self.running
        }

    def send_status_sync(self, force=False):
        now = time.time()
        if not force and now - self.last_status_push < self.status_push_interval:
            return
        self.last_status_push = now
        self._queue_payload(self.build_status_payload())

    def play_local_sound(self, sound_name):
        pass

    def get_config_value(self, *path, default=None):
        current = self.config
        for key in path:
            if not isinstance(current, dict) or key not in current:
                return default
            current = current[key]
        return current

    def get_return_to_spawn_delay(self):
        raw = self.get_config_value('follow_behavior', 'search_timeout', default=6)
        try:
            return max(1.0, float(raw))
        except (TypeError, ValueError):
            return 6.0

    def set_bot_state(self, new_state, activity_msg=None, audio_file="none"):
        if self.bot_state == new_state:
            return
        self.bot_state = new_state
        if activity_msg:
            self.send_activity_sync(activity_msg, audio_file != "none", audio_file)
        self.send_status_sync(force=True)

    def reset_runtime_tracking(self):
        self.bot_state = 'starting'
        self.current_target = None
        self.target_health = 100
        self.health = 100
        self.uber_charge = 0.0
        self.uber_ready = False
        self.uber_active = False
        self.uber_start_time = None
        self.heal_button_held = False
        self.last_environment_update = 0.0
        self.last_status_push = 0.0
        self.last_any_heal_target_time = 0.0
        self.last_heal_weapon_select = 0.0

    def select_heal_weapon(self, force=False):
        now = time.time()
        if force or now - self.last_heal_weapon_select >= 1.0:
            self.keyboard.tap('2')
            self.last_heal_weapon_select = now

    def press_heal_beam(self):
        if not self.heal_button_held:
            self.mouse.press(Button.left)
            self.heal_button_held = True

    def release_heal_beam(self):
        if self.heal_button_held:
            self.mouse.release(Button.left)
            self.heal_button_held = False

    def release_all_inputs(self):
        self.release_heal_beam()
        for key in ('w', 'a', 's', 'd'):
            try:
                self.keyboard.release(key)
            except Exception:
                pass
        try:
            self.keyboard.release(Key.space)
        except Exception:
            pass

    # ------------------------------------------------------------------ #
    #  WebSocket handler                                                   #
    # ------------------------------------------------------------------ #

    async def handle_ws(self, websocket):
        self.ws = websocket
        print("GUI connected.")
        try:
            async for message in websocket:
                data = json.loads(message)
                msg_type = data.get('type')

                if msg_type == 'config':
                    self.config = data.get('config', data)
                    self.follow_mode = str(
                        self.get_config_value('passive_mode', 'default_mode_on_startup', default='active')
                    ).strip().lower()
                    print(f"Config received.")

                elif msg_type == 'start':
                    if not self.running:
                        self.session_start = datetime.now()
                        self.running = True
                        threading.Thread(target=self.bot_main_loop, daemon=True).start()
                        await self.send_activity("Bot started!", False, "none")
                        await websocket.send(json.dumps(self.build_status_payload()))
                    else:
                        await self.send_activity("Bot is already running.", False, "none")

                elif msg_type == 'set_follow_mode':
                    self.follow_mode = data.get('mode', 'active')
                    await self.send_activity(f"Follow mode changed to {self.follow_mode}", True, "mode_switch")

                elif msg_type == 'stop':
                    self.running = False
                    self.release_all_inputs()
                    self.bot_state = 'standby'
                    await self.send_activity("Bot stopped.", False, "none")
                    await websocket.send(json.dumps(self.build_status_payload()))

                elif msg_type in {'ping', 'test'}:
                    await websocket.send(json.dumps({
                        'type': 'pong',
                        'msg': 'Bot server ready',
                        'running': self.running,
                        'mode': self.bot_state,
                        'uber': int(round(self.uber_charge))
                    }))

        except websockets.exceptions.ConnectionClosed:
            print("GUI disconnected. Awaiting new connection.")
        except Exception as e:
            print(f"WebSocket error: {e}")
        finally:
            self.ws = None

    # ------------------------------------------------------------------ #
    #  Bot main loop                                                       #
    # ------------------------------------------------------------------ #

    def bot_main_loop(self):
        self.reset_runtime_tracking()
        self.release_all_inputs()
        self.skip_intro()
        self.enter_spawn()
        self.equip_loadout()
        self.send_status_sync(force=True)
        self.send_activity_sync("Bot is in game and ready.", False, "none")

        while self.running:
            self.check_cpu_temp()
            if self.cpu_overheat:
                self.release_heal_beam()
                self.set_bot_state("cooling_down", "CPU too hot. Pausing.", "none")
                time.sleep(1)
                continue

            self.update_environment()
            self.handle_uber()

            timeout = self.get_return_to_spawn_delay()
            time_without_target = time.time() - self.last_any_heal_target_time

            if self.current_target:
                # Someone is visible and being healed — follow them
                self.set_bot_state("healing")
                self.select_heal_weapon()
                self.press_heal_beam()
                self.keyboard.release('s')
                self.keyboard.tap('w')

                # Back off slightly if target is overhealed
                if self.target_health and self.target_health > 142:
                    self.keyboard.tap('s')

            elif time_without_target < timeout:
                # Lost sight recently — scan around for someone
                self.set_bot_state("searching", "Scanning for a target...", "none")
                self.release_heal_beam()
                self.keyboard.tap('w')
                sweep = 8 if int(time_without_target * 4) % 2 == 0 else -8
                self.mouse.move(sweep, 0)

            else:
                # Nobody for a while — drift back toward spawn
                self.set_bot_state("returning_to_spawn", "No target found. Returning to spawn.", "none")
                self.release_heal_beam()
                self.keyboard.release('w')
                self.keyboard.tap('s')
                drift = 4 if int(time.time() * 3) % 2 == 0 else -4
                self.mouse.move(drift, 0)

            self.check_spies()
            self.send_status_sync()
            time.sleep(0.05)

        self.release_all_inputs()
        self.set_bot_state("standby")
        self.send_status_sync(force=True)

    # ------------------------------------------------------------------ #
    #  Startup routines                                                    #
    # ------------------------------------------------------------------ #

    def skip_intro(self):
        """
        Detects TF2 class selection screen by checking average screen brightness.
        The class selection screen has a dark overlay, so if brightness is low
        we click the Medic slot (fixed position, roughly center-right of screen).
        """
        if not self.vision:
            time.sleep(3)
            return

        self.send_activity_sync("Checking for class selection screen...")
        time.sleep(2)

        frame = self.vision.capture_screen()
        brightness = frame.mean()

        if brightness < 60:
            h, w = frame.shape[:2]
            # Medic is the 5th class button, roughly center-right of the screen
            medic_x = int(w * 0.62)
            medic_y = int(h * 0.55)
            self.mouse.position = (medic_x, medic_y)
            time.sleep(0.3)
            self.mouse.click(Button.left)
            time.sleep(0.4)
            self.mouse.click(Button.left)  # Double-click to confirm
            self.send_activity_sync("Selected Medic class.")
            time.sleep(1)
        else:
            self.send_activity_sync("Already in game, skipping class select.")

    def enter_spawn(self):
        """
        Binds F9 to 'kill' via the TF2 console, then presses it so the bot
        respawns at the team spawn point alongside the rest of the team.
        Only useful if the bot is already in a live match.
        """
        if not self.vision:
            time.sleep(2)
            return

        self.send_activity_sync("Binding respawn key and respawning...")

        # Open console
        self.keyboard.tap('`')
        time.sleep(0.5)

        # Type bind command character by character
        bind_cmd = 'bind F9 kill'
        for char in bind_cmd:
            self.keyboard.tap(char)
            time.sleep(0.04)

        self.keyboard.tap(Key.enter)
        time.sleep(0.2)

        # Close console
        self.keyboard.tap('`')
        time.sleep(0.3)

        # Press F9 to kill/respawn
        self.keyboard.tap(Key.f9)
        time.sleep(3)  # Wait for respawn timer

        self.send_activity_sync("Respawned at team spawn.")

    def equip_loadout(self):
        """Select medigun (slot 2) on spawn."""
        self.select_heal_weapon(force=True)
        time.sleep(0.2)

    # ------------------------------------------------------------------ #
    #  Environment update (HUD reading)                                    #
    # ------------------------------------------------------------------ #

    def update_environment(self):
        now = time.time()
        if now - self.last_environment_update < self.environment_update_interval:
            return
        self.last_environment_update = now

        if not self.vision:
            return

        try:
            snapshot = self.vision.read_hud_snapshot()
        except Exception as e:
            print(f"[update_environment] Vision error: {e}")
            return

        if not snapshot.ocr_available and not self.warned_about_ocr:
            self.warned_about_ocr = True
            self.send_activity_sync(
                "Tesseract OCR not found. HUD reading is disabled. Install Tesseract on this machine.",
                False,
                "none"
            )

        if snapshot.health is not None:
            self.health = snapshot.health

        if snapshot.heal_target_health is not None:
            self.target_health = snapshot.heal_target_health

        if snapshot.uber_charge is not None:
            self.uber_charge = snapshot.uber_charge

        if snapshot.heal_target_name:
            self.current_target = snapshot.heal_target_name
            self.last_any_heal_target_time = now
        else:
            self.current_target = None

        # Backstab detection — health dropped to 0 suddenly
        if self.health <= 0:
            self.send_activity_sync("Backstabbed!", True, "spy")

    # ------------------------------------------------------------------ #
    #  Uber                                                                #
    # ------------------------------------------------------------------ #

    def handle_uber(self):
        if self.uber_active:
            if time.time() - self.uber_start_time > 8:
                self.uber_active = False
                self.send_activity_sync("Uber ended. Duration: 8s.", True, "uber_cooldown")
            return

        if self.uber_charge >= 100 and not self.uber_ready:
            self.uber_ready = True
            self.send_activity_sync("Uber ready!", True, "uber_ready")

        ub_behavior = self.config.get('uber_behavior', 'Manual')
        threshold = 30
        try:
            threshold = int(self.get_config_value('uber', 'auto_pop_health_threshold', default=30))
        except (TypeError, ValueError):
            pass

        if ub_behavior == 'Auto-pop' and self.target_health < threshold and self.uber_ready:
            self.pop_uber()
        elif ub_behavior == 'Suggest' and self.current_target and self.uber_ready:
            self.send_activity_sync("Suggesting Uber!", True, "uber_ready")

    def pop_uber(self):
        self.select_heal_weapon(force=True)
        self.mouse.click(Button.right)
        self.uber_ready = False
        self.uber_active = True
        self.uber_charge = 0
        self.uber_start_time = time.time()
        self.send_activity_sync("Uber activated!", True, "uber_activated")

    # ------------------------------------------------------------------ #
    #  Spy check                                                           #
    # ------------------------------------------------------------------ #

    def check_spies(self):
        freq = self.config.get('spy_check_frequency', 10)
        try:
            freq = max(1, int(freq))
        except (TypeError, ValueError):
            freq = 10

        if time.time() % freq < 0.1:
            self.mouse.move(180, 0)
            time.sleep(0.05)
            self.mouse.move(180, 0)

    # ------------------------------------------------------------------ #
    #  System                                                              #
    # ------------------------------------------------------------------ #

    def check_stuck(self):
        return False

    def check_cpu_temp(self):
        try:
            temps = psutil.sensors_temperatures()
            if temps and 'coretemp' in temps:
                highest = max(t.current for t in temps['coretemp'])
                threshold = float(self.get_config_value('performance', 'cpu_temperature_throttle_threshold', default=85))
                self.cpu_overheat = highest > threshold
        except Exception:
            pass

    # ------------------------------------------------------------------ #
    #  Server startup                                                      #
    # ------------------------------------------------------------------ #

    async def run_server(self):
        print('Bot WebSocket server running on port 8765')
        async with websockets.serve(self.handle_ws, '0.0.0.0', 8765):
            await asyncio.Future()

    def run(self):
        self.loop = asyncio.new_event_loop()
        asyncio.set_event_loop(self.loop)
        self.loop.run_until_complete(self.run_server())


if __name__ == '__main__':
    bot = MedicAIBot()
    bot.run()