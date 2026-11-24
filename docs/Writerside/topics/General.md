# Onyx

Onyx shows which apps you use on your phone and computers, and for how long, in one dashboard, with goals,
streaks, reminders, focus mode, weekly and custom reports, and friends to compare with. Everything it records is end-to-end encrypted: the server
stores it but can't read it. Onyx is open source (AGPL-3.0) and runs on your own server.

The current version is **0.0**; the About page of every app shows its version and the server's
([Versions](Releasing.md#versions)).

## Using Onyx

| Article | What it covers |
|---|---|
| [Android App](Android-App.md) | Building, installing and updating the Android app, pointing it at your server |
| [Linux Agent](Linux-Agent.md) | Installing `onyx-agent` (Hyprland, Sway, GNOME, KDE, X11), unlocking it, starting it at login, updating |
| [Focus, Reminders and Reports](Focus-and-Reports.md) | Focus mode, break reminders and daily limits, weekly and custom reports |
| [Websites](Websites.md) | Recording which websites you use (browser extension, Windows and Linux) |
| [End-to-End Encryption](Encryption.md) | What is encrypted, keys and passphrase, recovery key, upgrading |

The web dashboard needs no installation: open your server's address in a browser.

## Running a server

| Article | What it covers |
|---|---|
| [Self-Hosting](Self-Hosting.md) | Docker, configuration, email, external logins, two-factor authentication, VPN setups, updating without losing data |
| [End-to-End Encryption](Encryption.md#upgrading-an-existing-server) | What changes for an existing server when encryption arrives |

## Developing Onyx

| Article | What it covers |
|---|---|
| [Developer Documentation](Developer-Documentation.md) | Tools, architecture, building and publishing |
| [Database](Database.md) | EF Core migrations for SQLite and SQL Server |
| [Releasing](Releasing.md) | Automatic versions, tags, the release workflow and Android signing |
| [Android Emulation](Android-Emulation.md) | Running the app in an emulator or on a phone from Rider |
| [Windows](Windows.md) | Setting up Windows to build the Windows app |
| [iOS](iOS.md) | Status of the iOS app |
| [Development Backlog](Onyx_Internal_Developer_Docs.md) | Bugs, issues and ideas, what is done, and the release test plan |
