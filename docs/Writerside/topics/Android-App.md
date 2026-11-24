# Android App

Releases (`onyx-android.apk`) start with the public server. To use your own server, either pick it on the login
page (**Change** next to the server), or build an APK that starts with your server preselected.

## Build and install your own APK

Once, install the Android SDK and a matching JDK (under 1 GB, into `~/Android`):

```bash
dotnet workload install maui-android
dotnet build src/Onyx.App/Onyx.App/Onyx.App.csproj -f net10.0-android -t:InstallAndroidDependencies \
  -p:AndroidSdkDirectory=$HOME/Android/Sdk -p:JavaSdkDirectory=$HOME/Android/jdk -p:AcceptAndroidSdkLicenses=True
```

Then build a signed APK:

```bash
packaging/android/build-apk.sh --server https://onyx.example.com
packaging/android/build-apk.sh --server http://10.8.0.1:8080 --private-network --install
```

| Option | Effect |
|---|---|
| `--server URL` | Server a fresh install starts with (MSBuild `-p:OnyxServerUrl=URL`). |
| `--private-network` | Also allows `http://` servers and certificates from CAs installed on the phone (`-p:OnyxPrivateNetwork=true`). For servers reachable only through a VPN or LAN; leave it off for builds you hand out. |
| `--install` | Installs over USB or wireless debugging (`adb install -r`). |

The first run creates a signing key in `~/.config/onyx-build/onyx-sideload.keystore` and asks for its password
(or reads `ONYX_KEYSTORE_PASSWORD`). **Keep the key.** Updates must be signed with it. With a different key
(for example the release key), Android only installs the APK after the old app is uninstalled.

Without `adb`, copy `out/onyx-android.apk` to the phone and open it. Android asks you to allow installs from
that app (Files, browser, ...).

## After installing

The app can also sign in with Google, Microsoft or GitHub when the server offers them: it opens the system
browser and comes back through `onyx://external`. On Android 13 and newer it asks for permission to show
notifications the first time you turn on a reminder or focus mode; reminders are checked every 15 minutes.

1. Open Onyx, check the server shown on the login page, then register or sign in. Enter your encryption
   passphrase (or choose one, if this is your first device). The app keeps the key in Android's secure storage, so
   the background upload can encrypt.
2. On the **Devices** page, tap **Grant usage access** and allow Onyx. Without it nothing can be collected.
3. Usage is uploaded every 30 minutes when a network is available. **Upload now** on the Devices page uploads at once.

## Updating

Build the new version with the same command and install it over the old one; **don't uninstall first**. An
update keeps the sign-in, the device registration and the record of what was already uploaded.

Uninstalling deletes those from the phone (the data on the server stays). After a reinstall the phone registers as
a **new device** and uploads the last 7 days again, so those days are counted twice. (Removing the old device on
the Devices page would also delete all of its older data.)

```bash
git pull
packaging/android/build-apk.sh --server https://onyx.example.com --install   # adb install -r keeps the app's data
```

An update only installs if it is signed with **the same key** (`~/.config/onyx-build/onyx-sideload.keystore`)
and has a higher version code; the script takes care of the version code. Without adb, open the new APK on the
phone and choose **Update**.

The first update to the version with end-to-end encryption asks for the passphrase once. Until then nothing is
uploaded; Android keeps the history, and the app uploads it after you unlock.

## See also

- [Self-Hosting](Self-Hosting.md)
- [End-to-End Encryption](Encryption.md)
- [Releasing](Releasing.md)
- [Android Emulation](Android-Emulation.md)
- [Overview of all articles](General.md)
