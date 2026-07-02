import sys
import json
from pathlib import Path

sys.path.append(str(Path(".agents/skills/oni-mcp-autonomous-iteration/scripts")))
from runtime_smoke import McpClient, result_json

def check_grid():
    c = McpClient("http://localhost:8788/mcp/")
    init = c.request("initialize", {
        "protocolVersion": "2025-11-25",
        "capabilities": {},
        "clientInfo": {"name": "check-grid", "version": "1"}
    })
    c.notify("notifications/initialized")
    
    # We query area snapshot with a custom rect focused around headquarters
    snap = result_json(c.call_tool("read_control", {
        "domain": "world",
        "action": "area_snapshot",
        "x": 88,
        "y": 140,
        "x1": 80,
        "y1": 136,
        "x2": 96,
        "y2": 145,
        "worldId": 0
    }))
    
    base_map = snap.get("maps", {}).get("base", {})
    legend = base_map.get("legend", {})
    rows = base_map.get("rows", [])
    
    print("=== GRID MAP ===")
    print("Legend:", json.dumps(legend, ensure_ascii=False))
    for row in rows:
        # The row content string is typically in 'rows' or 'r' or 'base' or 'p' or similar. 
        # In our earlier power map, it was row.get('p'). For base, let's print the entire row dictionary or its content key.
        print(f"y={row.get('y')}: {row}")

if __name__ == "__main__":
    check_grid()
