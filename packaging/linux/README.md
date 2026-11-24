# onyx-agent (Linux)

Headless usage collector for Linux desktops. It uploads to the Onyx server; the web dashboard shows the data.

Supported sessions: Hyprland, Sway, GNOME and KDE Plasma on Wayland, and any X11 desktop. GNOME on Wayland needs
the small Onyx Shell extension once: `onyx-agent gnome install`, then log out and in again. KDE needs nothing; the
agent loads a KWin script itself.

Install from a release archive (`onyx-agent-linux-x64.tar.gz` or `-arm64`):

    tar xzf onyx-agent-linux-x64.tar.gz && cd onyx-agent && ./install.sh

The agent runs as a systemd user service that starts with `graphical-session.target`. Hyprland or Sway started
directly (not through uwsm) never reach that target; add the autostart line `install.sh` prints to the
compositor's config (see the Linux Agent page of the documentation).

Commands: `onyx-agent login | unlock | run | status | logout | version | websites on|off | gnome install|remove`.
Reminders and focus mode (set on the dashboard's Focus page) show as desktop notifications through `notify-send`. With `websites on` and the Onyx browser
extension installed, browser time is split by website (domain only). Files: `~/.config/onyx/` (server, tokens; mode 600) and
`~/.local/state/onyx/pending.json` (data not uploaded yet).

Build the archive yourself:

    dotnet publish src/Onyx.Collector/Onyx.Collector.Linux -c Release -r linux-x64 --self-contained \
        -p:PublishSingleFile=true -o out/onyx-agent
    cp packaging/linux/onyx-agent.service packaging/linux/install.sh out/onyx-agent/
