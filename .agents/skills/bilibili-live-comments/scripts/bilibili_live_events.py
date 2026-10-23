#!/usr/bin/env python3
"""Read Bilibili WebSocket events into a credential-free local event stream.

Protocol references (wire format only; this is an independent implementation):
https://github.com/xfgryujk/blivedm/blob/master/blivedm/clients/ws_base.py
https://github.com/xfgryujk/blivedm/blob/master/blivedm/models/pb.py
Requires aiohttp. Negotiates zlib (protocol 2); optional brotli handles protocol 3.
No messages are sent to the room. Authentication keys remain in memory.
"""

import argparse
import asyncio
import base64
from collections import Counter, OrderedDict
import contextlib
from datetime import datetime, timezone
import fcntl
import hashlib
import json
import os
from pathlib import Path
import random
import signal
import struct
import time
import urllib.parse
import zlib

import aiohttp

try:
    import brotli
except ImportError:
    brotli = None


HEADER = struct.Struct(">IHHII")
MAX_FRAME = 4 * 1024 * 1024
USER_AGENT = "Mozilla/5.0"
WBI_INDICES = (46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45,
               35, 27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13)


def utc_now():
    return datetime.now(timezone.utc).isoformat()


def packet(operation, body=b"", version=1):
    if isinstance(body, dict):
        body = json.dumps(body, separators=(",", ":")).encode()
    return HEADER.pack(HEADER.size + len(body), HEADER.size, version, operation, 1) + body


def decode_packets(data, depth=0):
    if len(data) > MAX_FRAME or depth > 4:
        raise ValueError("packet size or nesting exceeds limit")
    offset = 0
    while offset < len(data):
        if len(data) - offset < HEADER.size:
            raise ValueError("truncated header")
        length, header_size, version, operation, _ = HEADER.unpack_from(data, offset)
        if not HEADER.size <= header_size <= length <= MAX_FRAME or offset + length > len(data):
            raise ValueError("invalid packet length")
        body = data[offset + header_size:offset + length]
        offset += length
        # Some servers append the legacy heartbeat text outside pack_len.
        if operation == 3 and data[offset:] == b"[object Object]":
            offset = len(data)
        if version == 2:
            decoder = zlib.decompressobj()
            unpacked = decoder.decompress(body, MAX_FRAME + 1)
            if len(unpacked) > MAX_FRAME or decoder.unconsumed_tail or not decoder.eof:
                raise ValueError("invalid or oversized zlib frame")
            yield from decode_packets(unpacked, depth + 1)
        elif version == 3:
            if brotli is None:
                raise ValueError("server sent brotli despite protocol 2 negotiation; install brotli")
            decoder = brotli.Decompressor()
            unpacked = bytearray()
            for index in range(0, len(body), 4096):
                unpacked.extend(decoder.process(body[index:index + 4096]))
                if len(unpacked) > MAX_FRAME:
                    raise ValueError("oversized brotli frame")
            if not decoder.is_finished():
                raise ValueError("truncated brotli frame")
            yield from decode_packets(bytes(unpacked), depth + 1)
        else:
            yield operation, body


def protobuf_fields(data):
    """Decode primitive protobuf fields, retaining repeated length-delimited fields."""
    offset = 0
    fields = {}

    def varint():
        nonlocal offset
        value = 0
        for shift in range(0, 70, 7):
            if offset >= len(data):
                raise ValueError("truncated protobuf varint")
            item = data[offset]
            offset += 1
            value |= (item & 127) << shift
            if item < 128:
                return value
        raise ValueError("oversized protobuf varint")

    while offset < len(data):
        tag = varint()
        field, wire = tag >> 3, tag & 7
        if field == 0:
            raise ValueError("invalid protobuf field")
        if wire == 0:
            value = varint()
        elif wire in (1, 2, 5):
            size = varint() if wire == 2 else (8 if wire == 1 else 4)
            if offset + size > len(data):
                raise ValueError("truncated protobuf field")
            value = data[offset:offset + size]
            offset += size
        else:
            raise ValueError("unsupported protobuf wire type")
        fields.setdefault(field, []).append(value)
    return fields


def pb_value(fields, number, default=0):
    return fields.get(number, [default])[0]


def pb_text(fields, number):
    value = pb_value(fields, number, b"")
    return value.decode("utf-8", errors="replace") if isinstance(value, bytes) else ""


def event_time(value):
    try:
        number = float(value)
        return (number / 1000 if number > 1e12 else number) if number > 0 else None
    except (ValueError, TypeError):
        return None


def normalized_events(command, room_id):
    """Only msg_type=1 of INTERACT_WORD represents entering the room."""
    cmd = str(command.get("cmd", "")).split(":", 1)[0]
    data = command.get("data") or {}
    if not isinstance(data, dict):
        raise ValueError("event data must be an object")
    event = {"kind": "", "cmd": cmd, "room_id": room_id, "uid": 0,
             "uname": "", "text": "", "event_time": None}
    identity = None
    if cmd == "DANMU_MSG":
        info = command["info"]
        event.update(kind="danmu", text=str(info[1]), uid=info[2][0], uname=str(info[2][1]),
                     event_time=event_time(info[0][4]))
        identity = info[0][5] if len(info[0]) > 5 else None
    elif cmd in ("INTERACT_WORD", "INTERACT_WORD_V2"):
        if cmd.endswith("_V2"):
            fields = protobuf_fields(base64.b64decode(data["pb"], validate=True))
            subtype = pb_value(fields, 5)
            event.update(uid=pb_value(fields, 1), uname=pb_text(fields, 2),
                         event_time=event_time(pb_value(fields, 7)))
        else:
            subtype = data.get("msg_type")
            event.update(uid=data.get("uid", 0), uname=str(data.get("uname", "")),
                         event_time=event_time(data.get("timestamp")))
        event["kind"] = {1: "enter", 2: "follow", 3: "share", 4: "special_follow",
                         5: "mutual_follow", 6: "like"}.get(subtype, "interaction")
        event["msg_type"] = subtype
    elif cmd == "SEND_GIFT":
        event.update(kind="gift", uid=data.get("uid", 0), uname=str(data.get("uname", "")),
                     gift_name=str(data.get("giftName", "")), count=data.get("num", 0),
                     event_time=event_time(data.get("timestamp")))
        identity = data.get("tid") or data.get("rnd")
    elif cmd == "SEND_GIFT_V2":
        fields = protobuf_fields(base64.b64decode(data["pb"], validate=True))
        for gift_bytes in fields.get(10, []):
            gift = protobuf_fields(gift_bytes)
            item = dict(event, kind="gift", uid=pb_value(fields, 1), uname=pb_text(fields, 2),
                        gift_name=pb_text(gift, 2), count=pb_value(gift, 3),
                        event_time=event_time(pb_value(gift, 10)))
            yield finish_event(item, pb_text(gift, 9) or pb_text(gift, 12))
        return
    elif cmd == "SUPER_CHAT_MESSAGE":
        event.update(kind="super_chat", uid=data.get("uid", 0),
                     uname=str((data.get("user_info") or {}).get("uname", "")),
                     text=str(data.get("message", "")), event_time=event_time(data.get("start_time")))
        identity = data.get("id")
    elif cmd == "LIKE_INFO_V3_CLICK":
        event.update(kind="like", uid=data.get("uid", 0), uname=str(data.get("uname", "")))
    elif cmd in ("LIVE", "PREPARING"):
        event["kind"] = "live_start" if cmd == "LIVE" else "live_end"
    else:
        return
    yield finish_event(event, identity)


def finish_event(event, identity):
    # Hash only allowlisted event data, never tokens or raw server payloads.
    event["identity_redacted"] = not event.get("uid") or "***" in event.get("uname", "")
    material = {k: v for k, v in event.items() if k != "cmd"}
    if identity:
        material["source_id"] = str(identity)
    if not identity and not event.get("event_time"):
        material["time_bucket"] = int(time.time() // 3)
    event["event_id"] = hashlib.sha256(json.dumps(material, sort_keys=True,
                                                ensure_ascii=False).encode()).hexdigest()[:24]
    event["received_at"] = utc_now()
    event["source"] = "bilibili_websocket"
    return event


class Listener:
    def __init__(self, args):
        self.args = args
        self.queue = asyncio.Queue(maxsize=256)
        self.seen = OrderedDict()
        self.stop = asyncio.Event()
        self.heartbeat_ack_monotonic = time.monotonic()
        self.health = {"pid": os.getpid(), "room_id": args.room_id, "started_at": utc_now(),
                       "connected": False, "auth_code": None, "last_heartbeat_ack": None,
                       "last_event": None, "last_frame_at": None, "frames": 0,
                       "events": 0, "dropped_events": 0, "reconnect_count": 0,
                       "protocol": 2, "event_path": str(args.events), "commands": {}}
        self.command_counts = Counter()

    def write_health(self):
        self.health["updated_at"] = utc_now()
        self.health["queue_size"] = self.queue.qsize()
        self.health["commands"] = dict(self.command_counts)
        tmp = self.args.health.with_suffix(".tmp")
        tmp.write_text(json.dumps(self.health, ensure_ascii=False) + "\n")
        tmp.replace(self.args.health)

    async def health_loop(self):
        while not self.stop.is_set():
            self.write_health()
            try:
                await asyncio.wait_for(self.stop.wait(), 2)
            except asyncio.TimeoutError:
                pass

    async def writer(self):
        with self.args.events.open("a", encoding="utf-8", buffering=1) as stream:
            while True:
                event = await self.queue.get()
                try:
                    line = json.dumps(event, ensure_ascii=False)
                    stream.write(line + "\n")
                    print(line, flush=True)
                finally:
                    self.queue.task_done()

    async def server_config(self, session):
        if self.args.auth_config and self.args.auth_config.is_file():
            metadata = self.args.auth_config.stat()
            if metadata.st_mode & 0o077 or metadata.st_uid != os.getuid():
                raise RuntimeError("unsafe_auth_config_permissions")
            result = json.loads(self.args.auth_config.read_text())
            if result.get("room_id") != self.args.room_id or not result.get("uid"):
                raise RuntimeError("auth_config_room_or_identity_mismatch")
            if not result.get("token") or not result.get("host_list"):
                raise RuntimeError("incomplete_auth_config")
            self.health["authenticated_identity"] = True
            self.health["config_source"] = "browser_temporary_token"
            return result

        async def fetch(url, **kwargs):
            async with session.get(url, **kwargs) as response:
                response.raise_for_status()
                return await response.json()

        nav = await fetch("https://api.bilibili.com/x/web-interface/nav")
        images = nav["data"]["wbi_img"]
        keys = "".join(images[name].rsplit("/", 1)[1].split(".", 1)[0]
                       for name in ("img_url", "sub_url"))
        salt = "".join(keys[index] for index in WBI_INDICES)
        params = {"id": str(self.args.room_id), "type": "0", "wts": str(int(time.time()))}
        query = urllib.parse.urlencode(sorted(params.items()))
        params["w_rid"] = hashlib.md5((query + salt).encode()).hexdigest()
        result = await fetch("https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo", params=params)
        if result.get("code") != 0:
            raise RuntimeError("danmu_config_code=" + str(result.get("code")))
        self.health["authenticated_identity"] = False
        self.health["config_source"] = "public_anonymous"
        return result["data"]

    async def heartbeat(self, socket):
        while True:
            if time.monotonic() - self.heartbeat_ack_monotonic > 70:
                self.health["connected"] = False
                await socket.close()
                return
            await socket.send_bytes(packet(2))
            self.health["last_heartbeat_sent"] = utc_now()
            await asyncio.sleep(25)

    def receive_command(self, command):
        if not isinstance(command, dict):
            self.health["parse_errors"] = self.health.get("parse_errors", 0) + 1
            return
        cmd = str(command.get("cmd", "")).split(":", 1)[0]
        self.command_counts[cmd] += 1
        try:
            events = list(normalized_events(command, self.args.room_id))
        except (KeyError, IndexError, ValueError, TypeError, AttributeError):
            self.health["parse_errors"] = self.health.get("parse_errors", 0) + 1
            print(json.dumps({"warning": "event_parse_error", "cmd": cmd}), flush=True)
            return
        for event in events:
            identifier = event["event_id"]
            if identifier in self.seen:
                continue
            self.seen[identifier] = None
            if len(self.seen) > 4096:
                self.seen.popitem(last=False)
            if self.queue.full():
                self.health["dropped_events"] += 1
                print('{"warning":"event_queue_full"}', flush=True)
                continue
            self.queue.put_nowait(event)
            self.health["last_event"] = event["received_at"]
            self.health["events"] += 1
            if event["kind"] in ("danmu", "enter"):
                self.health["last_" + event["kind"]] = event["received_at"]
                if not event["identity_redacted"]:
                    self.health["last_named_" + event["kind"]] = event["received_at"]

    async def connect(self, session, config, retry):
        hosts = config["host_list"]
        host = hosts[retry % len(hosts)]
        url = f"wss://{host['host']}:{host.get('wss_port', 443)}/sub"
        self.health.update(connected=False, auth_code=None, host=host["host"],
                           last_heartbeat_ack=None)
        async with session.ws_connect(url, timeout=15, receive_timeout=40, max_msg_size=MAX_FRAME,
                                      headers={"User-Agent": config.get("user_agent", USER_AGENT)}) as socket:
            self.heartbeat_ack_monotonic = time.monotonic()
            auth = {"uid": config.get("uid", 0), "roomid": self.args.room_id, "protover": 2,
                    "platform": "web", "type": 2, "buvid": "", "key": config["token"]}
            await socket.send_bytes(packet(7, auth))
            heartbeat_task = asyncio.create_task(self.heartbeat(socket))

            def check_heartbeat(task):
                if not task.cancelled() and task.exception():
                    self.health["connected"] = False
                    asyncio.create_task(socket.close())

            heartbeat_task.add_done_callback(check_heartbeat)
            auth_deadline = time.monotonic() + 15
            try:
                async for message in socket:
                    if self.stop.is_set():
                        return
                    if self.health["auth_code"] is None and time.monotonic() > auth_deadline:
                        raise RuntimeError("authentication_timeout")
                    if message.type == aiohttp.WSMsgType.BINARY:
                        self.health["frames"] += 1
                        self.health["last_frame_at"] = utc_now()
                        for operation, body in decode_packets(message.data):
                            if operation == 8:
                                result = json.loads(body)
                                self.health["auth_code"] = result.get("code")
                                if result.get("code") != 0:
                                    raise RuntimeError("websocket_auth_code=" + str(result.get("code")))
                                self.health["connected"] = True
                                print('{"status":"websocket_authenticated","auth_code":0}', flush=True)
                            elif operation == 3:
                                self.heartbeat_ack_monotonic = time.monotonic()
                                self.health["last_heartbeat_ack"] = utc_now()
                                self.health["heartbeat_acks"] = self.health.get("heartbeat_acks", 0) + 1
                            elif operation == 5:
                                self.receive_command(json.loads(body))
                            elif operation == 6:
                                raise RuntimeError("server_disconnect")
                    elif message.type in (aiohttp.WSMsgType.CLOSED, aiohttp.WSMsgType.CLOSE,
                                          aiohttp.WSMsgType.ERROR):
                        raise RuntimeError("websocket_closed")
                raise RuntimeError("websocket_stream_ended")
            finally:
                heartbeat_task.cancel()
                with contextlib.suppress(asyncio.CancelledError):
                    await heartbeat_task

    async def run(self):
        background = [asyncio.create_task(self.writer()), asyncio.create_task(self.health_loop())]
        owner = asyncio.current_task()

        def check_background(task):
            if not task.cancelled() and task.exception():
                self.health["fatal_error"] = type(task.exception()).__name__
                owner.cancel()

        for task in background:
            task.add_done_callback(check_background)
        timeout = aiohttp.ClientTimeout(total=15)
        headers = {"User-Agent": USER_AGENT, "Referer": f"https://live.bilibili.com/{self.args.room_id}",
                   "Origin": "https://live.bilibili.com"}
        retry = 0
        try:
            async with aiohttp.ClientSession(timeout=timeout, headers=headers, trust_env=True) as session:
                while not self.stop.is_set():
                    try:
                        config = await self.server_config(session)
                        await self.connect(session, config, retry)
                    except (aiohttp.ClientError, asyncio.TimeoutError, OSError, RuntimeError,
                            ValueError, KeyError, zlib.error) as error:
                        # Never stringify network errors: URLs or server responses can include secrets.
                        self.health["connected"] = False
                        self.health["last_error"] = type(error).__name__
                        self.health["last_error_at"] = utc_now()
                        print(json.dumps({"warning": "listener_reconnecting", "error": type(error).__name__,
                                          "retry": retry + 1}), flush=True)
                    if self.stop.is_set():
                        break
                    retry += 1
                    self.health["reconnect_count"] = retry
                    try:
                        await asyncio.wait_for(self.stop.wait(), min(30, 2 ** min(retry, 4)) + random.random())
                    except asyncio.TimeoutError:
                        pass
        finally:
            for task in background:
                task.cancel()
            await asyncio.gather(*background, return_exceptions=True)
            self.health.update(connected=False, stopped_at=utc_now())
            self.write_health()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--room-id", type=int, default=31882282)
    parser.add_argument("--events", type=Path, default=Path("/tmp/oni-live-events.jsonl"))
    parser.add_argument("--health", type=Path, default=Path("/tmp/oni-live-events-health.json"))
    parser.add_argument("--auth-config", type=Path, help="Optional mode-600 browser-issued token JSON; never a cookie file.")
    args = parser.parse_args()
    os.umask(0o077)
    lock = args.health.with_suffix(".lock").open("a+")
    try:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        raise SystemExit("A listener already owns this health path.")
    listener = Listener(args)

    async def run():
        loop = asyncio.get_running_loop()
        task = asyncio.create_task(listener.run())
        for sig in (signal.SIGINT, signal.SIGTERM):
            loop.add_signal_handler(sig, task.cancel)
        with contextlib.suppress(asyncio.CancelledError):
            await task

    asyncio.run(run())
    if listener.health.get("fatal_error"):
        raise SystemExit("Listener stopped: " + listener.health["fatal_error"])


if __name__ == "__main__":
    main()
