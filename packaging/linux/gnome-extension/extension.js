// Onyx: lets onyx-agent ask GNOME Shell which window has the focus. On Wayland, GNOME doesn't tell ordinary
// programs; this extension answers one D-Bus method on the session bus (only processes of the same user can call it).
//
//   gdbus call --session --dest org.gnome.Shell --object-path /io/github/theonyxteam/Onyx \
//     --method io.github.theonyxteam.Onyx.GetFocus
//   -> ('firefox', 'firefox.desktop', uint32 4242, 'Onyx - Mozilla Firefox')

import Gio from 'gi://Gio';
import Shell from 'gi://Shell';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

const INTERFACE = `
<node>
  <interface name="io.github.theonyxteam.Onyx">
    <method name="GetFocus">
      <arg type="s" direction="out" name="wmClass"/>
      <arg type="s" direction="out" name="appId"/>
      <arg type="u" direction="out" name="pid"/>
      <arg type="s" direction="out" name="title"/>
    </method>
  </interface>
</node>`;

export default class OnyxExtension extends Extension {
    enable() {
        this._dbus = Gio.DBusExportedObject.wrapJSObject(INTERFACE, this);
        this._dbus.export(Gio.DBus.session, '/io/github/theonyxteam/Onyx');
    }

    disable() {
        this._dbus?.unexport();
        this._dbus = null;
    }

    GetFocus() {
        const window = global.display.focus_window;
        if (!window)
            return ['', '', 0, ''];
        const app = Shell.WindowTracker.get_default().get_window_app(window);
        const pid = window.get_pid();
        return [window.get_wm_class() ?? '', app?.get_id() ?? '', pid > 0 ? pid : 0, window.get_title() ?? ''];
    }
}
