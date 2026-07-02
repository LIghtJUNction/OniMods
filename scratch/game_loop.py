import json
import sys
import time
import os
from pathlib import Path

sys.path.append(str(Path(".agents/skills/oni-mcp-autonomous-iteration/scripts")))
from runtime_smoke import McpClient, result_json

sys.path.append(str(Path(".agents/skills/bilibili-live-comments/scripts")))
from bilibili_live_comments import load_dotenv, fetch_room_messages, send_room_message, format_message

ROOM_ID = "31882282"

def main():
    load_dotenv()
    cookie = os.environ.get("BILI_COOKIE")
    
    c = McpClient("http://localhost:8788/mcp/")
    init = c.request("initialize", {
        "protocolVersion": "2025-11-25",
        "capabilities": {},
        "clientInfo": {"name": "game-loop", "version": "1"}
    })
    c.notify("notifications/initialized")
    
    # 1. Check current game status
    snap = result_json(c.call_tool("colony_control", {"domain": "snapshot", "action": "get", "profile": "minimal"}))
    print(f"=== 游戏状态 ===")
    print(json.dumps(snap, ensure_ascii=False, indent=2))
    
    # 2. Check alerts
    alerts = result_json(c.call_tool("colony_control", {"domain": "diagnostic", "action": "alerts", "limit": 20}))
    print(f"=== 告警信息 ===")
    print(json.dumps(alerts, ensure_ascii=False, indent=2))
    
    # 3. Check Bilibili comments
    print(f"=== Bilibili 直播间弹幕 ===")
    try:
        res = fetch_room_messages(ROOM_ID, 8, cookie)
        room_messages = res.get("data", {}).get("room", [])
        for item in room_messages[-5:]:  # show last 5 messages
            print(format_message(item))
    except Exception as e:
        print(f"获取弹幕失败: {e}")
        room_messages = []

    # 4. Generate status comment and send to Bilibili
    cycle = snap.get("cycle", 0)
    dupes = snap.get("metrics", {}).get("dupes", 0)
    food = snap.get("metrics", {}).get("foodKcal", 0)
    
    # We keep it short to avoid Bilibili length limit
    danmu_msg = f"> 周期{cycle} | 人口{dupes} | 食物{food/1000:.1f}k卡"
    if alerts and isinstance(alerts, list):
        danmu_msg += f" | 告警:{alerts[0].get('message', '')[:10]}"
    
    danmu_msg = danmu_msg[:40] # cap at 40 chars
    
    if cookie:
        print(f"发送弹幕: {danmu_msg}")
        try:
            send_res = send_room_message(ROOM_ID, danmu_msg, 8, cookie)
            print(f"发送结果: {send_res.get('code')} - {send_res.get('message')}")
        except Exception as e:
            print(f"发送弹幕失败: {e}")
    else:
        print("未设置 BILI_COOKIE，跳过发送弹幕。")

if __name__ == "__main__":
    main()
