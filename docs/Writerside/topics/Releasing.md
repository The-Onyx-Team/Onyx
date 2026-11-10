# Releasing

Pushing a version tag builds every device app and publishes a GitHub release
(`.github/workflows/release.yml`):

```bash
git tag v1.2.0 && git push origin v1.2.0
```

| Asset | Contents |
|---|---|
| `onyx-android.apk` | Signed Android app, for direct download |
| `onyx-windows-x64.zip` | Self-contained, unpackaged Windows app |
| `onyx-agent-linux-{x64,arm64}.tar.gz` | Linux agent with systemd unit and installer |
| `onyx-browser-extension.zip` | Browser extension (website recording), for loading unpacked or store upload |
| `SHA256SUMS` | Checksums |

The web app's downloads page links to `/releases/latest/download/<asset>`, so keep these names. The Android
App Bundle for Google Play is kept as the workflow artifact `onyx-android-play-bundle`.

## Android signing

Create an upload key once and keep it safe; losing it means a new app listing on Google Play.

```bash
keytool -genkeypair -v -keystore onyx.keystore -alias onyx -keyalg RSA -keysize 4096 -validity 10000
base64 -w0 onyx.keystore   # value for ANDROID_KEYSTORE_BASE64
```

Repository secrets: `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`,
`ANDROID_KEY_PASSWORD`. The Android package name is `com.onyx_team.onyx.app`.

## Store checklist

Google Play:
- Developer account; new personal accounts must run a closed test before production access.
- Privacy policy URL: `https://<server>/privacy` (fill in the operator details in `PrivacyPage.razor` first).
- Data safety form: declare exactly what the privacy notice lists (app usage per app and hour, device name and platform, account email).
- Usage access (`PACKAGE_USAGE_STATS`) is granted by the user in system settings; describe why in the listing.
- Account deletion: in the app (Settings) and on the web (`/settings`).

Browser extension (see [Websites](Websites.md)):
- Chrome Web Store and Edge Add-ons: upload `onyx-browser-extension.zip` after removing the `key` field from
  `manifest.json`, then add the assigned extension ids to `BrowserBridge.ChromeExtensionIds`.
- Firefox: submit to addons.mozilla.org (id `websites@onyx-team.github.io`). Release builds of Firefox only install
  signed add-ons.
- All stores: single purpose "usage statistics for the Onyx app", privacy policy URL, and the disclosure that
  browsing activity (domains of the active tab) is collected. Chrome shows the `tabs` permission as "Read your
  browsing history".
- Windows: the zip contains `onyx-native-host.exe` (Native AOT, built on the Windows runner).

Microsoft Store: package the Windows app as MSIX (`WindowsPackageType=MSIX`) with the Identity values from
Partner Center in `Platforms/Windows/Package.appxmanifest`. For direct downloads, sign `Onyx.App.exe`
(for example with Azure Trusted Signing) so SmartScreen does not block it.
