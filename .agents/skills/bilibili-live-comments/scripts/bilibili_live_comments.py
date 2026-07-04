#!/usr/bin/env python3
import argparse
import json
import os
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path


LENGTH_RETRY_CODES = {1003212}


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
            os.environ.setdefault(key.strip(), value.strip().strip("'\""))
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


def split_message(message, max_len):
    message = (message or "").strip()
    if not message:
        return []
    if len(message) <= max_len:
        return [message]

    prefix = "> " if message.startswith("> ") else ""
    body = message[len(prefix) :].strip() if prefix else message
    chunk_len = max(4, max_len - len(prefix))
    chunks = []
    while body:
        if len(body) <= chunk_len:
            chunks.append(prefix + body)
            break

        cut = best_cut(body, chunk_len)
        part = body[:cut].strip()
        if part:
            chunks.append(prefix + part)
        body = body[cut:].strip()
    return chunks


def best_cut(text, limit):
    window = text[:limit]
    for marks in ("。！？；，、,.!?; ", "\n"):
        positions = [window.rfind(mark) for mark in marks]
        cut = max(positions)
        if cut >= max(4, limit // 2):
            return cut + 1
    return limit


def send_with_segments(room_id, message, timeout, cookie, max_len, delay):
    pending = split_message(message, max_len)
    results = []
    index = 0
    while index < len(pending):
        part = pending[index]
        result = send_room_message(room_id, part, timeout, cookie)
        code = result.get("code")
        if code in LENGTH_RETRY_CODES and len(part) > 8:
            smaller = split_message(part, max(8, max_len // 2))
            if len(smaller) > 1:
                pending[index : index + 1] = smaller
                continue
        results.append({"part": index + 1, "message": part, "result": result})
        index += 1
        if index < len(pending) and delay > 0:
            time.sleep(delay)

    ok = all(item["result"].get("code") == 0 for item in results)
    return {"ok": ok, "segmented": len(results) > 1, "count": len(results), "results": results}


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
    parser.add_argument("--assistant-send", help="Send one assistant status comment; prefixes '> ' internally.")
    parser.add_argument("--send-max-length", type=int, default=32)
    parser.add_argument("--send-delay", type=float, default=1.0)
    args = parser.parse_args()

    cookie = os.environ.get("BILI_COOKIE")
    send_message = args.send
    if args.assistant_send:
        send_message = args.assistant_send.strip()
        if not send_message.startswith(">"):
            send_message = "> " + send_message

    if send_message:
        if not cookie:
            raise SystemExit("BILI_COOKIE missing. Ask user for a fresh Bilibili cookie and save it in .env.")
        result = send_with_segments(
            args.room_id,
            send_message,
            args.timeout,
            cookie,
            max(8, args.send_max_length),
            max(0.0, args.send_delay),
        )
        print(json.dumps(result, ensure_ascii=False))
        return

    seen = set()
    first = True
    while True:
        try:
            payload = fetch_room_messages(args.room_id, args.timeout, cookie)
            items = payload.get("data", {}).get("room", [])
            new_items = []
            for item in items:
                mid = message_id(item)
                if mid in seen:
                    continue
                seen.add(mid)
                new_items.append(item)
            if args.print_existing:
                for item in items:
                    print(format_message(item))
                return
            if not first:
                for item in new_items:
                    print(format_message(item), flush=True)
            first = False
        except Exception as exc:
            print(f"[poll-error] {exc}", file=sys.stderr, flush=True)
        time.sleep(max(5.0, args.interval))


if __name__ == "__main__":
    main()
