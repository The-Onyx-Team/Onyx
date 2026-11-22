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

# graphical-session.target is only reached by sessions that start it (most systemd-managed desktops do;
# for compositors started from a TTY, add "systemctl --user start graphical-session.target" to their startup).
systemctl --user enable onyx-agent.service
# restart, not start: on an update the old binary would otherwise keep running until the next login.
systemctl --user restart onyx-agent.service
echo "onyx-agent is running. Check it with: onyx-agent status / journalctl --user -u onyx-agent"
