# iPhone and iPad

There is no native iOS app yet. On an iPhone or iPad you can install the **dashboard** to the home screen
instead: it then opens full screen, without Safari's address bar, like an app.

1. Open your server's address in **Safari** (on iOS 16.4 and newer also Chrome, Edge or Firefox).
2. Tap **Share**, then **Add to Home Screen**, then **Add**.
3. Open Onyx from the home screen, sign in and enter your passphrase.

The installed dashboard is the same web dashboard as in the browser: it decrypts your data on the device, shows
every page and works with goals, reports, focus settings and friends. It needs a connection to your server and
HTTPS (browsers only offer the encryption functions on secure connections).

## Limits

- **It doesn't record anything.** iOS gives screen time only to native apps with Apple's Screen Time entitlement,
  not to web apps, so the iPhone or iPad doesn't appear as a device. Usage from your other devices shows as usual.
- The home screen app keeps its own storage, separate from Safari: sign in once more there. Without
  **Stay unlocked** iOS forgets the key when it closes the app, so it asks for the passphrase again.
- No reminders or focus notifications: they come from the devices that record.

The same works on Android, Windows, macOS and Linux: Chromium browsers offer **Install Onyx** in the address bar
or menu. The server provides the web app manifest (`manifest.webmanifest`) and icons; there is no service worker,
so the installed dashboard always loads the server's current version.

## Native app

A native iOS app (the MAUI project has `ios` and `maccatalyst` targets) needs a Mac to build and Apple's Family
Controls entitlement to read screen time, and is not planned yet.

## See also

- [Developer Documentation](Developer-Documentation.md)
- [End-to-End Encryption](Encryption.md)
- [Overview of all articles](General.md)
