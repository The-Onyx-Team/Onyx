# Linux Agent

.NET MAUI does not run on Linux, so Linux devices use `onyx-agent`, a small headless collector
(`src/Onyx.Collector/Onyx.Collector.Linux`). It runs as a systemd user service and uploads to the server; the
web dashboard is the UI.

## Supported sessions

| Session | Focused window | Idle detection |
|---|---|---|
| Hyprland | compositor IPC socket | cursor and window unchanged (Hyprland has no idle query) |
| Sway | i3 IPC (`SWAYSOCK`) | screen lock only |
| X11 (any desktop) | `_NET_ACTIVE_WINDOW` via libX11 | XScreenSaver extension |
| GNOME / KDE on Wayland | not supported yet | |

GNOME and KDE on Wayland do not let ordinary programs see the focused window. Supporting them needs a GNOME
Shell extension or a KWin script that reports it over D-Bus.

## How it works

The agent samples the focused app every 2 seconds, adds the time to hourly buckets in
`~/.local/state/onyx/pending.json`, and uploads every 5 minutes. The server replaces buckets it already has, so
the current hour is re-sent until it is over and nothing is counted twice. Offline time is uploaded later.
App names, categories and icons come from `.desktop` files.

## Install and use

```bash
tar xzf onyx-agent-linux-x64.tar.gz && cd onyx-agent && ./install.sh
onyx-agent status
journalctl --user -u onyx-agent
```
