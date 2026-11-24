# Releasing

## Versions

The version number lives in one place, `Directory.Build.props` at the repository root (`OnyxVersion`, currently
**0.0**). The server, the apps and the Linux agent are built with it; .NET adds the git commit to the
informational version, so every build is identifiable. It shows on:

- the **About** page (`/about`, linked from the app bar, the account menu, Settings, the sign-in page and the
  website footer), with the app's version, the server's version (from `GET /api/info`) and a warning when they
  differ;
- `onyx-agent version` and `onyx-agent status`;
- the Android app's version name.

### Automatic versions

Every push to `master` whose build and tests pass is a new version (`.github/workflows/version.yml`): the
**minor** number goes up by one from the last tag (v0.3 → v0.4), the commit is tagged, the device apps are
released (below) and the server image is published as `ghcr.io/the-onyx-team/onyx:latest` and `:<version>`.
`.github/scripts/next-version.sh` computes the number.

A **major** version is ordered in one of three ways:

- `[major]` anywhere in the commit message (v0.7 → v1.0);
- running the *Version* workflow by hand with *bump = major*;
- setting `OnyxVersion` in `Directory.Build.props` above the last tag, e.g. to `1.0` (that number is released next).

`OnyxVersion` is otherwise only what local builds show; tagged builds get the tag's number through
`-p:Version`, and the Docker image through the `ONYX_VERSION` build argument. Tags pushed by the workflow don't
start other workflows, so `version.yml` calls `release.yml` and `docker.yaml` itself.

To release by hand (for example from another branch), push a tag:

```bash
git tag -a v0.1 -m "Onyx 0.1"
git push origin v0.1        # starts the release workflow below
```

Without the `ANDROID_*` secrets the release has no Android app (the job warns instead of failing), so the rest
is still published.

## Release builds

Every version builds every device app and publishes a GitHub release (`.github/workflows/release.yml`). The
Windows app is built with `-p:OnyxWindowsOnly=true`, which limits the MAUI project to its Windows target: a restore
for `win-x64` would otherwise also resolve the Android target and fail without an error message.

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

## See also

- [Android App](Android-App.md)
- [Linux Agent](Linux-Agent.md)
- [Developer Documentation](Developer-Documentation.md)
- [Development Backlog](Onyx_Internal_Developer_Docs.md)
- [Overview of all articles](General.md)
