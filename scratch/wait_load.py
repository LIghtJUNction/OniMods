import json
import sys
import time
from pathlib import Path

sys.path.append(str(Path(".agents/skills/oni-mcp-autonomous-iteration/scripts")))
from runtime_smoke import McpClient, result_json

def wait_for_load():
    c = McpClient("http://localhost:8788/mcp/")
    init = c.request("initialize", {
        "protocolVersion": "2025-11-25",
        "capabilities": {},
        "clientInfo": {"name": "wait-load", "version": "1"}
    })
    c.notify("notifications/initialized")
    
    deadline = time.time() + 120
    print("Waiting for game to load save...")
    while time.time() < deadline:
        try:
            res = result_json(c.call_tool("colony_control", {"domain": "snapshot", "action": "get", "profile": "minimal"}))
            text = res.get("text", "")
            if "Game not initialized" not in text and "cycle" in res:
                print("Game loaded successfully!")
                print(json.dumps(res, ensure_ascii=False, indent=2))
                return True
            else:
                print(f"Still loading... Status: {res}")
        except Exception as e:
            print(f"Error querying: {e}")
        time.sleep(3)
    
    print("Timeout waiting for game to load.")
    return False

if __name__ == "__main__":
    if not wait_for_load():
        sys.exit(1)
