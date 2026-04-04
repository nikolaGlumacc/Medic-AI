import os
import time
import subprocess
import psutil

class WatchdogProcess:
    def __init__(self, executable="hl2.exe"):
        self.executable = executable
        self.tf2_process = None

    def start_game(self, server_ip=None):
        cmd = ["steam", "-applaunch", "440"]
        if server_ip:
            cmd.extend(["+connect", server_ip])
        print(f"Watchdog: Starting TF2 with {cmd}")
        # In actual practice, steam might detach.
        # We find the process by name instead.
        subprocess.Popen(cmd)
        time.sleep(15) # Wait for it to spin up
        self.find_process()

    def find_process(self):
        for proc in psutil.process_iter(['name', 'pid']):
            if proc.info['name'] == self.executable:
                self.tf2_process = proc
                return True
        self.tf2_process = None
        return False

    def is_running(self):
        if self.tf2_process:
            return self.tf2_process.is_running()
        return self.find_process()

    def monitor_loop(self, callback_on_crash):
        while True:
            if not self.is_running():
                print("Watchdog: TF2 Crash Detected!")
                callback_on_crash()
            time.sleep(5)
