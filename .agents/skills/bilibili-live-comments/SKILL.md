---
name: bilibili-live-comments
description: Use when reading, polling, or sending Bilibili live-room comments/danmu. Uses public APIs for reading when possible and .env BILI_COOKIE for authenticated send.
---

# Bilibili Live Comments

Use this skill when the user asks to read, poll, monitor, or send comments in a Bilibili live room.

## Safety And Credentials

- Use public room comment APIs for read-only comment checks when they work.
- Sending comments requires a logged-in Bilibili cookie containing `bili_jct`.
- Load the cookie from `.env` as `BILI_COOKIE=...`.
- If `.env` is missing `BILI_COOKIE` and the user asks to send, ask the user for a fresh cookie.
- Do not print, quote, commit, or upload the cookie.
- Ensure `.env` is ignored by git before storing cookies:

```bash
git check-ignore -v .env
```

If not ignored, add `.env` to `.gitignore`.

## Commands

Read comments once with existing messages printed:

```bash
python .agents/skills/bilibili-live-comments/scripts/bilibili_live_comments.py --room-id 31882282 --print-existing
```

Poll comments periodically:

```bash
python .agents/skills/bilibili-live-comments/scripts/bilibili_live_comments.py --room-id 31882282 --interval 60
```

Send one comment using `.env`:

```bash
python .agents/skills/bilibili-live-comments/scripts/bilibili_live_comments.py --room-id 31882282 --send 'message'
```

## Notes

- The script automatically searches upward from the current working directory for `.env`.
- The script never writes cookies; it only reads `BILI_COOKIE`.
- Prefer concise status updates after sending: report whether Bilibili returned `code: 0`, not the full response.
