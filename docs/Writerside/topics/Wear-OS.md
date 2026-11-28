# Wear OS

Onyx for Wear OS records which apps you use on the watch, like the phone app does on the phone, and uploads them
end-to-end encrypted. The watch appears as a device of its own on the dashboard. It has no dashboard itself:
everything is shown on your phone, computer or in the browser.

It needs Wear OS 2 or newer (Android 9+) and works without the phone app: it talks to your server over the
watch's own connection (Wi-Fi, LTE, or the phone's Bluetooth connection).

## Build and install

Set up the Android SDK as for the [phone app](Android-App.md), then build the watch APK with the same script and
signing key:

```bash
packaging/android/build-apk.sh --wear --server https://onyx.example.com
```

`--server` and `--private-network` work as for the phone app. The APK is `out/onyx-wear.apk`.

Watches install APKs only over `adb`. On the watch, turn on **Developer options** (Settings → System → About →
tap **Build number** seven times), then **ADB debugging** and **Debug over Wi-Fi**. The watch shows an address:

```bash
adb connect 192.168.1.42:5555        # newer watches: adb pair <address shown under "Pair new device"> first
packaging/android/build-apk.sh --wear --server https://onyx.example.com --install
```

## Link the watch

Typing a password and a passphrase on a watch is no fun, so the watch is linked with a code instead:

1. Open Onyx on the watch. Check the server shown (tap it to change it), then tap **Get a code**.
2. On your phone, computer or in the browser: **Devices → Link a watch**, enter the code (`7K2QM-X9D4R`) and tap
   **Link**. That device must be signed in and unlocked.
3. The watch signs in to your account and starts uploading.

The watch gets tokens of its own and your encryption key, encrypted with a key only the watch has (ECDH). The
code is the fingerprint of the watch's key, so the device you enter it on can tell whether the server handed it
the watch's key or another one. Codes are valid for 10 minutes. **Unlink** on the watch signs it out; removing it
on the Devices page deletes its data.

## Usage access

Like the phone, the watch needs **usage access** to see which apps are used. After linking, Onyx shows **Allow usage
access** and opens the watch's settings. Some watches have no such screen; grant it from a computer instead:

```bash
adb shell appops set com.onyx_team.onyx.wear GET_USAGE_STATS allow
```

## What it records

App time in hourly buckets, how often each app is opened and how often the watch is unlocked (watches with a lock
screen), from Android's own event log; the watch face (home screen) is not counted. The watch uploads every 30
minutes when it has a connection, and when you tap **Upload now**. On the first upload it sends what the watch
still has from the past week.

Reminders, focus mode and Stay in control are not available on the watch.

## See also

- [Android App](Android-App.md)
- [End-to-End Encryption](Encryption.md)
- [Overview of all articles](General.md)
