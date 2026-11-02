# Recording and archive notes

Read only for an explicitly requested recording or archive task. These are existing host-specific notes; confirm the installed recorder, desktop session and uploader limits before applying them.

## Recording and cloud archive discipline (Linux/Hyprland)

When live footage recording or archive staging is active:
- **OpenScreen Wayland automation**: Launch `/home/lightjunction/.local/opt/openscreen/OpenScreen.AppImage record` under a PTY wrapper with explicit Wayland environment (`WAYLAND_DISPLAY=wayland-1`, `XDG_CURRENT_DESKTOP=Hyprland`, etc.). Handle the desktop portal card selection via `ydotool` or window manager dispatcher.
- **Pre-resume capture verification**: Confirm `sourceKind=window` and `capture started` in the log, and extract a test frame via ffmpeg (`ffmpeg -ss 00:00:01 -i <file> -vframes 1 /tmp/verify.jpg`) to ensure the ONI game window is actively being captured without a black screen before resuming game simulation.
- **Graceful termination**: Send `stop\n` to PTY stdin or `kill -INT <pid>` (or `touch /tmp/oni_record.stop`). Never `kill -9` recording processes as it corrupts MP4 moov atom indices.
- **100 MiB slice & Google Drive upload standard**:
  - Slice large videos into ~90-second segments via lossless stream copy:
    ```bash
    ffmpeg -y -i input.mp4 -c copy -f segment -segment_time 90 -reset_timestamps 1 part%02d.mp4
    ```
    (Guarantees segment sizes stay between 30~60 MiB, strictly below the 100 MiB cloud upload limit).
  - Upload slices: `gdrive mutate upload <part.mp4> --parent <folderId>`.
  - Verify via `gdrive readonly info <fileId> --json` to verify size and presence.
  - **Do not delete local raw footage until all slice uploads are verified in Google Drive**.
