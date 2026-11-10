# Onyx Websites (browser extension)

Reports the domain of the active tab of the focused browser window to the Onyx collector on the same computer
(the Windows app or `onyx-agent` on Linux), via native messaging. See
`docs/Writerside/topics/Websites.md` for how it fits together.

One Manifest V3 source works in Chrome, Edge, Brave and other Chromium browsers (service worker) and in Firefox
128+ (`background.scripts`).

## Try it locally

1. Turn website recording on: Onyx app → Devices → "Record websites", or `onyx-agent websites on`.
2. Load the extension:
   - Chromium: `chrome://extensions` → Developer mode → "Load unpacked" → this folder. The `key` in
     `manifest.json` gives the unpacked build the fixed id `nkgaflfkpndlfchgfnjmjdkjdfpihnei`, which the native
     host manifests allow.
   - Firefox: `about:debugging#/runtime/this-firefox` → "Load Temporary Add-on" → `manifest.json`.
3. Click the extension's icon: it should say "Connected".

## Publishing

- Chrome Web Store / Edge Add-ons: upload a zip of this folder **without** the `key` field. The store assigns a
  new id; add it to `BrowserBridge.ChromeExtensionIds` in `Onyx.Collector.Core` so the host manifests allow it.
- Firefox: submit to addons.mozilla.org; the id `websites@onyx-team.github.io` is fixed in the manifest.
- Both stores require the privacy policy URL (`/privacy` of your deployment) and a statement that browsing
  activity (domains only) is collected for the single purpose of usage statistics.
