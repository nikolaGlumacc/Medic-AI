#!/usr/bin/env python3
import sys
import os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), 'bot'))

from bot_server import MedicAIBot

if __name__ == '__main__':
    print("Starting MedicAI Bot Server...")
    bot = MedicAIBot()
    bot.run()