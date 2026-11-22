#!/bin/sh
# Installs or updates onyx-agent for the current user (no root needed) and enables the systemd user service.
# Run from the extracted release archive: ./install.sh. Updating keeps the sign-in (~/.config/onyx) and the
# data not uploaded yet (~/.local/state/onyx).
set -eu

here=$(cd "$(dirname "$0")" && pwd)
bin_dir="${HOME}/.local/bin"
unit_dir="${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"

install -Dm755 "$here/onyx-agent" "$bin_dir/onyx-agent"
install -Dm644 "$here/onyx-agent.service" "$unit_dir/onyx-agent.service"
systemctl --user daemon-reload

if ! "$bin_dir/onyx-agent" status >/dev/null 2>&1; then
    echo "Sign in first:"
    "$bin_dir/onyx-agent" login
fi

# The service starts with graphical-session.target. Sessions managed by systemd reach it (GNOME, KDE, and
# Hyprland/Sway started through uwsm); a compositor launched directly by a display manager or from a TTY does
# not, and the target refuses a manual start. Those sessions start the service from their autostart instead.
systemctl --user enable onyx-agent.service
# restart, not start: on an update the old binary would otherwise keep running until the next login.
systemctl --user restart onyx-agent.service
echo "onyx-agent is running. Check it with: onyx-agent status / journalctl --user -u onyx-agent"

if ! systemctl --user is-active --quiet graphical-session.target; then
    autostart="systemctl --user import-environment WAYLAND_DISPLAY DISPLAY HYPRLAND_INSTANCE_SIGNATURE SWAYSOCK XDG_CURRENT_DESKTOP && systemctl --user start onyx-agent.service"
    echo
    echo "Your session does not start graphical-session.target, so onyx-agent will not start by itself at the"
    echo "next login. Add this to your compositor's autostart:"
    echo "  Hyprland (hyprland.conf):  exec-once = sh -c '$autostart'"
    echo "  Sway (config):             exec sh -c '$autostart'"
    echo "Or start the compositor through uwsm. Details: https://the-onyx-team.github.io/Onyx/linux-agent.html"
fi
