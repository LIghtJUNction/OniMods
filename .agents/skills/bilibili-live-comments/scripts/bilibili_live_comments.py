#!/usr/bin/env python3
import argparse
import json
import os
from pathlib import Path
import sys
import time
import urllib.parse
import urllib.request


def load_dotenv():
    for parent in [Path.cwd(), *Path.cwd().parents]:
        env_path = parent / ".env"
        if not env_path.is_file():
            continue
        for raw_line in env_path.read_text().splitlines():
            line = raw_line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            key, value = line.split("=", 1)
            key = key.strip()
            value = value.strip().strip("'\"")
            os.environ.setdefault(key, value)
        return env_path
    return None


def csrf_from_cookie(cookie):
    for part in cookie.split(";"):
        key, _, value = part.strip().partition("=")
        if key == "bili_jct" and value:
            return value
    return None


def fetch_room_messages(room_id, timeout, cookie=None):
    query = urllib.parse.urlencode({"roomid": room_id})
    url = f"https://api.live.bilibili.com/ajax/msg?{query}"
    headers = {"User-Agent": "oni-mcp-comment-poller/1"}
    if cookie:
        headers["Cookie"] = cookie
    req = urllib.request.Request(url, headers=headers)
    with urllib.request.urlopen(req, timeout=timeout) as response:
        return json.loads(response.read().decode())


def send_room_message(room_id, message, timeout, cookie):
    csrf = csrf_from_cookie(cookie)
    if not csrf:
        raise RuntimeError("BILI_COOKIE is missing bili_jct csrf token")

    data = urllib.parse.urlencode(
        {
            "bubble": 0,
            "msg": message,
            "color": 16777215,
            "mode": 1,
            "fontsize": 25,
            "rnd": int(time.time()),
            "roomid": room_id,
            "csrf": csrf,
            "csrf_token": csrf,
        }
    ).encode()
    headers = {
        "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
        "Cookie": cookie,
        "Origin": "https://live.bilibili.com",
        "Referer": f"https://live.bilibili.com/{room_id}",
        "User-Agent": "oni-mcp-comment-poller/1",
    }
    req = urllib.request.Request(
        "https://api.live.bilibili.com/msg/send",
        data=data,
        headers=headers,
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=timeout) as response:
        return json.loads(response.read().decode())


def message_id(item):
    return item.get("id_str") or f"{item.get('timeline')}|{item.get('uid')}|{item.get('text')}"


def format_message(item):
    timeline = item.get("timeline", "")
    name = item.get("nickname") or item.get("user", {}).get("base", {}).get("name") or "unknown"
    text = item.get("text", "")
    return f"[{timeline}] {name}: {text}"


def main():
    load_dotenv()
    parser = argparse.ArgumentParser(description="Poll Bilibili live room comments.")
    parser.add_argument("--room-id", default="31882282")
    parser.add_argument("--interval", type=float, default=60.0)
    parser.add_argument("--timeout", type=float, default=8.0)
    parser.add_argument("--print-existing", action="store_true")
    parser.add_argument("--send", help="Send one live-room comment, then exit.")
    args = parser.parse_args()
    cookie = os.environ.get("BILI_COOKIE")

    if args.send:
        if not cookie:
            raise SystemExit("BILI_COOKIE is missing. Ask the user for a Bilibili cookie and save it in .env.")
        result = send_room_message(args.room_id, args.send, args.timeout, cookie)
        print(json.dumps(result, ensure_ascii=False))
        return

    seen = set()
    first = True
    while True:
        try:
            payload = fetch_room_messages(args.room_id, args.timeout, cookie)
            items = payload.get("data", {}).get("room", []) if isinstance(payload, dict) else []
            new_items = []
            for item in items:
                mid = message_id(item)
                if mid in seen:
                    continue
                seen.add(mid)
                new_items.append(item)

            if first and not args.print_existing:
                first = False
            else:
                for item in new_items:
                    print(format_message(item), flush=True)
                first = False
        except Exception as exc:
            print(f"[poll-error] {exc}", file=sys.stderr, flush=True)

        time.sleep(max(5.0, args.interval))


if __name__ == "__main__":
    main()
