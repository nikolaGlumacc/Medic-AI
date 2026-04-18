#!/usr/bin/env python3
import socket
import os
import subprocess
import json
import logging

logging.basicConfig(level=logging.INFO, format="[%(levelname)s] %(message)s")
logger = logging.getLogger("MedicAI-Verifier")

def check_port(port):
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.settimeout(1)
        try:
            s.bind(("0.0.0.0", port))
            return False # Not in use
        except socket.error:
            return True # In use

def get_owner_process(port):
    try:
        output = subprocess.check_output(f"netstat -ano | findstr :{port}", shell=True).decode()
        lines = output.strip().split("\n")
        for line in lines:
            if "LISTENING" in line:
                pid = line.strip().split()[-1]
                proc_name = subprocess.check_output(f"tasklist /FI \"PID eq {pid}\" /NH", shell=True).decode()
                return f"{proc_name.strip().split()[0]} (PID: {pid})"
    except:
        return "Unknown"
    return "None"

def verify():
    print("============================================")
    print("   MedicAI Connection Verifier (LAPTOP)")
    print("============================================\n")

    # 1. Check Binding Integrity
    flask_in_use = check_port(5000)
    ws_in_use = check_port(8766)

    print(f"[1] Flask API (5000): {'ONLINE' if flask_in_use else 'OFFLINE'}")
    if flask_in_use:
        print(f"    ↳ Owner: {get_owner_process(5000)}")
    
    print(f"[2] WebSocket (8766): {'ONLINE' if ws_in_use else 'OFFLINE'}")
    if ws_in_use:
        print(f"    ↳ Owner: {get_owner_process(8766)}")

    print("\n--- Network Configuration ---")
    hostname = socket.gethostname()
    ip_address = socket.gethostbyname(hostname)
    print(f"Host: {hostname}")
    print(f"Local IP: {ip_address}")

    # 2. Check Firewall (Windows Specific)
    print("\n--- Firewall Probe ---")
    try:
        fw_status = subprocess.check_output("netsh advfirewall show allprofiles state", shell=True).decode()
        if "ON" in fw_status:
            print("[WARN] Firewall is ACTIVE.")
            print("Action: Ensure 'Python.exe' is allowed or port 5000/8766 are open.")
        else:
            print("[OK] Firewall is DISABLED.")
    except:
        print("[ERORR] Could not check firewall status.")

    print("\n--- Binding Check ---")
    # Check if we can reach ourselves on 0.0.0.0
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
            s.settimeout(1)
            s.connect((ip_address, 5000))
            print(f"[OK] Internal Loopback to {ip_address}:5000 successful.")
    except:
        print(f"[FAIL] Internal Loopback to {ip_address}:5000 failed.")
        print("Reason: Server might be bound to 127.0.0.1 (Localhost only).")

    print("\n============================================")
    print("DIAGNOSTIC COMPLETE")
    print("============================================")

if __name__ == "__main__":
    verify()
