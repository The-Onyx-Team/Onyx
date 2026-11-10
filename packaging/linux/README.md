# onyx-agent (Linux)

Headless usage collector for Linux desktops. It uploads to the Onyx server; the web dashboard shows the data.

Supported sessions: Hyprland and Sway (Wayland), any X11 desktop. GNOME and KDE on Wayland don't expose the
focused window to ordinary programs and are not supported yet.

Install from a release archive (`onyx-agent-linux-x64.tar.gz` or `-arm64`):

    tar xzf onyx-agent-linux-x64.tar.gz && cd onyx-agent && ./install.sh

Commands: `onyx-agent login | run | status | logout | websites on|off`. With `websites on` and the Onyx browser
extension installed, browser time is split by website (domain only). Files: `~/.config/onyx/` (server, tokens; mode 600) and
`~/.local/state/onyx/pending.json` (data not uploaded yet).

Build the archive yourself:

    dotnet publish src/Onyx.Collector/Onyx.Collector.Linux -c Release -r linux-x64 --self-contained \
        -p:PublishSingleFile=true -o out/onyx-agent
    cp packaging/linux/onyx-agent.service packaging/linux/install.sh out/onyx-agent/
