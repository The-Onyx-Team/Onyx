# Websites

On Windows and Linux, Onyx can split browser time by website. It is off by default, turned on per computer, and
records the **domain** of the active tab only (`github.com`, never the full address). Mobile devices don't record
websites.

## How it fits together

```
Browser extension ──native messaging──▶ native host ──local pipe──▶ collector ──upload──▶ server
(active tab's domain)                   (started by the browser)    (Windows app / onyx-agent)
```

1. **Browser extension** (`browser-extension/`, Manifest V3 for Chrome, Edge, Brave and Firefox) reports
   `{focused, domain}` whenever the active tab, its address or the focused window changes, and once a minute.
   Private windows are never reported.
2. **Native messaging host**: the browser starts it and writes the messages to its stdin. On Linux that is
   `onyx-agent native-host` (via a launcher script); on Windows `onyx-native-host.exe` next to the app (Native AOT,
   `src/Onyx.Collector/Onyx.Collector.NativeHost`). The host adds the browser's process id (its parent process,
   skipping `cmd.exe` on Windows) and forwards each report as a JSON line.
3. **Bridge**: the collector listens on a named pipe (`onyx-browser-<user>`; a Unix socket on Linux), restricted
   to the current user (`BrowserBridgeServer`). `BrowserActivity` keeps the last report per connected extension.
4. **Tracker**: when the focused window belongs to a browser process that has a report with `focused: true`, the
   sample is credited to that domain (`IWebsiteSource`). Matching by process id keeps two browsers apart; several
   profiles of one browser share a process, and the focused profile's report wins.
5. **Upload**: buckets are keyed by app, domain and hour. A browser's hour becomes several `UsageEntryDto`s with
   a `Domain`; the time without a website has none. They add up to the browser's time.

The collectors read the user's excluded websites from the encrypted settings before each upload and drop those
domains locally. The server can't: everything about websites is end-to-end encrypted ([Encryption](Encryption.md)).

## Server

- The domains travel inside the encrypted usage rows: one row per device, app and hour, with the time per website
  inside it. The server stores the row without being able to read it.
- **An upload replaces whole app hours**: a row replaces the stored row of the same app and hour. Collectors
  therefore always send complete hours (`PendingUsageStore.Acknowledge` keeps or drops an app's hour as a whole,
  `OnyxApiClient.ChunkByHour` never splits an hour across requests).
- Excluded websites are part of the encrypted settings. Excluding a domain or deleting the history is done by the
  dashboard: it decrypts the affected rows, moves their time to the browser's time without a website, encrypts
  them again and uploads them (`E2EWebsiteSettings`), so browser totals stay the same.

## Turning it on

- **Windows**: Devices page → "Record websites in browsers". This writes host manifests to the app data folder and
  registers them under `HKCU\Software\{Google\Chrome,Chromium,Microsoft\Edge,BraveSoftware\Brave-Browser,Vivaldi,Mozilla}\NativeMessagingHosts`.
  Turning it off removes them.
- **Linux**: `onyx-agent websites on`, then `systemctl --user restart onyx-agent`. Manifests go to the
  `NativeMessagingHosts` folders of installed Chromium browsers under `~/.config` and to
  `~/.mozilla/native-messaging-hosts` (also LibreWolf and Waterfox). Browsers installed as Flatpak or Snap can't
  start host programs outside their sandbox and are not supported.

Then install the extension. Its popup says whether it reaches the collector.

## Extension ids

The native host only accepts the extension ids listed in its manifest:

- Firefox: `websites@onyx-team.github.io` (fixed in `manifest.json`).
- Chromium: the unpacked build has the fixed id `nkgaflfkpndlfchgfnjmjdkjdfpihnei` (from the `key` in
  `manifest.json`). The Chrome Web Store and Edge Add-ons assign their own ids when the extension is published; add
  them to `BrowserBridge.ChromeExtensionIds` and ship a new app/agent version, which rewrites the manifests on start.

## See also

- [Linux Agent](Linux-Agent.md)
- [End-to-End Encryption](Encryption.md)
- [Self-Hosting](Self-Hosting.md)
- [Overview of all articles](General.md)
