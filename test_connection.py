import asyncio
import websockets
import json

async def test_connection():
    try:
        uri = "ws://127.0.0.1:8765"
        async with websockets.connect(uri) as websocket:
            print("✅ Connected to bot server successfully!")

            # Send a test message
            test_msg = {"type": "ping", "data": "test"}
            await websocket.send(json.dumps(test_msg))
            print("✅ Sent test message")

            # Try to receive response
            try:
                response = await asyncio.wait_for(websocket.recv(), timeout=5.0)
                print(f"✅ Received response: {response}")
            except asyncio.TimeoutError:
                print("⚠️  No response received (expected for ping)")

            print("✅ WebSocket connection test completed successfully!")

    except Exception as e:
        print(f"❌ Connection failed: {e}")

if __name__ == "__main__":
    asyncio.run(test_connection())