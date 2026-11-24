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
| GNOME on Wayland | the Onyx GNOME Shell extension (D-Bus) | Mutter IdleMonitor |
| KDE Plasma 5/6 on Wayland | a KWin script the agent loads itself (D-Bus) | freedesktop ScreenSaver |

GNOME and KDE on Wayland do not let ordinary programs see the focused window, so they need a small helper inside
the desktop:

- **GNOME** (45 or newer): run `onyx-agent gnome install` once, log out and in again, and restart the agent
  (`systemctl --user restart onyx-agent`). The extension (`packaging/linux/gnome-extension`) answers one D-Bus
  method on your session bus, `io.github.theonyxteam.Onyx.GetFocus`, with the focused window's app id, process
  and title. It sends nothing anywhere itself. `onyx-agent gnome remove` uninstalls it.
- **KDE Plasma**: nothing to install. When the agent starts it takes the name `io.github.theonyxteam.OnyxAgent`
  on the session bus and loads `packaging/linux/kwin/onyx-focus.js` through KWin's scripting interface; the script
  calls the agent whenever another window becomes active, and is unloaded when the agent stops.

`onyx-agent status` shows which session was detected, and for GNOME whether the extension answers.

## How it works

The agent samples the focused app every 2 seconds, adds the time to hourly buckets in
`~/.local/state/onyx/pending.json`, and uploads every 5 minutes. The server replaces buckets it already has, so
the current hour is re-sent until it is over and nothing is counted twice. Offline time is uploaded later.
App names, categories and icons come from `.desktop` files.

## Reminders and focus mode

The agent checks every 30 seconds whether a reminder is due (for example a break after an hour without one) or
whether focus mode is on while a distracting app is in front, and shows a desktop notification through
`notify-send` (libnotify) or, without it, `gdbus`. The rules are part of the encrypted settings (Focus page on the
dashboard) and are read again every few minutes. Daily limits count the time on this computer since the agent
started that day.

## Websites

`onyx-agent websites on` registers the agent with the installed browsers so the Onyx browser extension can report
the active tab's domain; restart the service afterwards. `onyx-agent websites off` removes the registration. See
[Websites](Websites.md).

## Install and use

```bash
tar xzf onyx-agent-linux-x64.tar.gz && cd onyx-agent && ./install.sh
onyx-agent status
journalctl --user -u onyx-agent
```

`onyx-agent login` asks for your password and then your **encryption passphrase** (the one you chose in the
browser or app). The agent encrypts everything before uploading, so it needs the key; it is kept in
`~/.config/onyx/credentials.json` (mode 600). If your account has no encryption yet, sign in once in the browser
or app first. For an agent that was signed in before encryption existed, run:

```bash
onyx-agent unlock                 # or: onyx-agent unlock --recovery-key
systemctl --user restart onyx-agent
```

Without the key the agent keeps recording into its queue and uploads once it is unlocked; `onyx-agent status`
shows `Encrypted: no key yet`.

## Starting at login

`install.sh` enables the systemd user service `onyx-agent.service`, which starts together with
`graphical-session.target`. Whether that target is reached depends on how the desktop starts:

| Session | Starts by itself? |
|---|---|
| GNOME, KDE, other systemd-managed sessions | yes |
| Hyprland or Sway started through **uwsm** (e.g. the "uwsm-managed" session in the display manager) | yes |
| Hyprland or Sway started directly (by SDDM/GDM/greetd, or `Hyprland`/`sway` from a TTY) | **no** |

`install.sh` prints a hint when the target is not active. `systemctl --user start graphical-session.target` does
not help: systemd refuses to start that target by hand. Instead, let the compositor start the service when it
starts. The first command hands the session's variables (display, compositor socket) to systemd; the agent needs
them to see the focused window.

Hyprland, `hyprland.conf`:

```
exec-once = sh -c 'systemctl --user import-environment WAYLAND_DISPLAY HYPRLAND_INSTANCE_SIGNATURE XDG_CURRENT_DESKTOP && systemctl --user start onyx-agent.service'
```

Hyprland with a Lua config (`hyprland.lua`), inside the `hl.on("hyprland.start", ...)` block:

```lua
hl.exec_cmd("sh -c 'systemctl --user import-environment WAYLAND_DISPLAY HYPRLAND_INSTANCE_SIGNATURE XDG_CURRENT_DESKTOP && systemctl --user start onyx-agent.service'")
```

Sway, `~/.config/sway/config`:

```
exec sh -c 'systemctl --user import-environment WAYLAND_DISPLAY SWAYSOCK XDG_CURRENT_DESKTOP && systemctl --user start onyx-agent.service'
```

X11 window managers: the same with `DISPLAY` in their startup file (e.g. `~/.xinitrc`, before the window manager).

The service stays under systemd either way, so it is restarted after a crash and logs to
`journalctl --user -u onyx-agent`. After the next login, `onyx-agent status` should show `Signed in: yes` and your
session. If you later switch to uwsm, remove the line again.

## Updating

Extract the new archive and run `./install.sh` again. It replaces the binary and restarts the service; the
sign-in (`~/.config/onyx/`) and data not uploaded yet (`~/.local/state/onyx/pending.json`) stay. Usage recorded
while the old version was running is uploaded by the new one.

Built from source instead:

```bash
git pull
dotnet publish src/Onyx.Collector/Onyx.Collector.Linux -c Release -r linux-x64 --self-contained \
    -p:PublishSingleFile=true -o out/onyx-agent
cp packaging/linux/onyx-agent.service packaging/linux/install.sh out/onyx-agent/
(cd out/onyx-agent && ./install.sh)
```

Don't run `onyx-agent logout` for an update: it deletes the stored sign-in and the key.

The first version with end-to-end encryption needs `onyx-agent unlock` once after the update (see above).

## See also

- [End-to-End Encryption](Encryption.md)
- [Websites](Websites.md)
- [Self-Hosting](Self-Hosting.md)
- [Releasing](Releasing.md)
- [Overview of all articles](General.md)
