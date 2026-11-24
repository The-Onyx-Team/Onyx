// Onyx: tells onyx-agent which window is active on KDE Plasma (Wayland), where ordinary programs can't see it.
// The agent loads this KWin script itself (through KWin's D-Bus scripting interface) and receives the calls.
// Works with Plasma 6 (windowActivated) and Plasma 5 (clientActivated).

function report(window) {
    if (!window) {
        callDBus("io.github.theonyxteam.OnyxAgent", "/io/github/theonyxteam/OnyxAgent",
            "io.github.theonyxteam.OnyxAgent", "Focus", "", "", 0, "");
        return;
    }
    callDBus("io.github.theonyxteam.OnyxAgent", "/io/github/theonyxteam/OnyxAgent",
        "io.github.theonyxteam.OnyxAgent", "Focus",
        String(window.resourceClass || ""), String(window.desktopFileName || ""),
        window.pid > 0 ? window.pid : 0, String(window.caption || ""));
}

if (workspace.windowActivated) {
    workspace.windowActivated.connect(report);
    report(workspace.activeWindow);
} else {
    workspace.clientActivated.connect(report);
    report(workspace.activeClient);
}
