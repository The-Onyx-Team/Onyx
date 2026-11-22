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

1. Open Onyx, check the server shown on the login page, then register or sign in.
2. On the **Devices** page, tap **Grant usage access** and allow Onyx. Without it nothing can be collected.
3. Usage is uploaded every 30 minutes when a network is available. **Upload now** on the Devices page uploads at once.
