import asyncio
import json

import websockets


async def test_connection():
    try:
        uri = "ws://127.0.0.1:8765"
        async with websockets.connect(uri) as websocket:
            print("[OK] Connected to bot server successfully!")

            test_msg = {"type": "ping", "data": "test"}
            await websocket.send(json.dumps(test_msg))
            print("[OK] Sent test message")

            response = await asyncio.wait_for(websocket.recv(), timeout=5.0)
            print(f"[OK] Received response: {response}")
            print("[OK] WebSocket connection test completed successfully!")
    except Exception as e:
        print(f"[ERR] Connection failed: {e}")
        raise


if __name__ == "__main__":
    asyncio.run(test_connection())
