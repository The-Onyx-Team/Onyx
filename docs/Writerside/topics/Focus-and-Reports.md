# Focus, Reminders and Reports

Everything on this page is built on your devices from your encrypted data; the rules and saved reports are part of
the encrypted settings, so the server can't read them either.

## Focus mode

Focus mode nudges you whenever a **distracting** app comes to the front (by default the categories Social, Video
and Games; pick your own apps and categories on the **Focus** page). Start it on any device for 25 minutes up to
4 hours, or set regular times ("weekdays 9:00 to 12:00"); all your devices pick it up. You can limit it to some
devices.

Onyx doesn't block apps; it reminds you, about once every 10 minutes per app.

## Reminders

| Kind | When it notifies |
|---|---|
| **Take a break after** | After that long without a break of 5 minutes, and again every as long. |
| **Daily limit of** | Once a day, when the time today on the device reaches it. |

Each reminder can count all apps or only some apps and categories, can be limited to some devices, and can have
your own text.

## Where notifications appear

| Device | How often it checks | Notes |
|---|---|---|
| Android | every 15 minutes | Android doesn't allow background checks more often, so nudges can come a little late. Android 13 and newer ask for permission to notify. |
| Windows | every 30 seconds | while Onyx runs, also hidden in the notification area |
| Linux (`onyx-agent`) | every 30 seconds | desktop notifications through `notify-send` |
| Browser | – | The web dashboard only edits the settings. |

Each device decides on its own from what it records itself; a daily limit counts that device's time.

## Stay in control

Before an app opens, the phone asks a question; the app opens once you answer it correctly, **Not now** (or Back)
goes to the home screen. A wrong answer brings a new question. Set it up on the **Focus** page:

| Setting | |
|---|---|
| Questions | Mental arithmetic, capitals of the world, or your own questions (vocabulary, facts to learn). Answers ignore case, accents and punctuation. |
| Ask again after | After a right answer the app opens without a question for this long (1 minute to 1 hour). |
| Exempt | Apps and categories that open as usual. The phone app, Onyx, the home screen, settings and keyboards never ask. |
| Devices | Only some phones; none picked means all. |

For your own questions, separate several right answers with a vertical bar: `colour|color`.

It works in the **Android app** only, and only after you allow it once: Android shows an app which other app is
in front immediately only through an accessibility service. Onyx's service receives nothing but that (no screen
content, nothing you type). On the Focus page tap **Open accessibility settings** and turn on **Onyx: Stay in
control**. On Android 13 and newer, apps installed outside Google Play first need **App info → ⋮ → Allow restricted
settings**. Changes saved on the phone apply at once; changes from another device arrive within a few minutes.

Like everything else, the settings (including your own questions) are end-to-end encrypted.

## Reports

The **Reports** page has two tabs:

- **Weekly report:** the week's screen time per day and device, top apps against the week before, categories,
  the trend over 8 weeks, the time of day and each device. **PDF** saves it as an A4 document.
- **My reports:** put together your own: a period (last 7 or 30 days, this or last month, the last 3 months, this
  year, or any dates up to a year), only some devices, apps or categories, and the parts you want (every day, top
  apps, top websites, categories, weekly trend, time of day, each device). Compared with the period of the same
  length just before. Save a report to run it again any time, and download it as a PDF.

## See also

- [End-to-End Encryption](Encryption.md)
- [Linux Agent](Linux-Agent.md)
- [Android App](Android-App.md)
- [Overview of all articles](General.md)
