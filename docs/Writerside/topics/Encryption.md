# End-to-End Encryption

Onyx encrypts everything it records on the user's devices before uploading it. The server stores the
ciphertext and keeps it in sync between devices, but has no key to read it, and neither does the server's operator.

## What is encrypted

| Encrypted (only the user's devices can read it) | Plain text (the server needs it) |
|---|---|
| Usage: which app, for how long, which websites | Account: user name, email, password hash, profile picture |
| App names, categories and icons | Devices: name, platform, last upload |
| Settings: own app names, categories, hidden apps, excluded websites, goals, sharing level | At which hours data exists, and how many apps per hour |
| Numbers shared with friends (encrypted for each friend) | Who is friends with whom, group memberships |

## Keys

- A random 32-byte **master key** is created when the user turns on encryption. Encryption and tag keys are derived
  from it with HMAC-SHA256.
- The server stores the master key only **wrapped** with a key derived from the passphrase (PBKDF2-HMAC-SHA256,
  600,000 iterations, random salt). The passphrase never leaves the device.
- The **recovery key** is the master key itself, written as 52 characters of Crockford base32. It unlocks the data
  and sets a new passphrase if the passphrase is forgotten. Without passphrase and recovery key, the data is lost.
- Every blob is AES-256-GCM: `version (1) || nonce (12) || ciphertext || tag (16)`. The associated data binds it
  to its place (user, device, app tag and hour), so the server can't move blobs around undetected.
- Apps are identified by a **tag**, a keyed hash of the app identifier, so uploads can replace an app's hour
  without the server learning which app it is.
- Friends: each user has an ECDH P-256 key pair (the private key encrypted with the master key). Two friends derive
  the same AES key from their keys; the **safety code** on the Friends page lets them check that nobody swapped
  public keys.

## Where the key is kept

| Client | Where | Notes |
|---|---|---|
| Android, Windows | Secure storage | The background upload needs it. |
| Browser | Session storage, or local storage with "Stay unlocked" | Signing out removes it. Needs HTTPS (or localhost): browsers only offer WebCrypto on secure connections. |
| Linux agent | `~/.config/onyx/credentials.json` (mode 600) | `onyx-agent unlock` |

The code is in `src/Onyx.Collector/Onyx.Collector.Core/Crypto/` (.NET) and
`src/Onyx.App/Onyx.App.Shared/wwwroot/js/onyx.js` (WebCrypto, same format). The web dashboard runs in the browser
(Blazor WebAssembly) so that decryption happens there.

## Upgrading an existing server

1. Update the server. Accounts keep working; their data is still plain text.
2. Each user signs in once (web or app) and chooses a passphrase. That device encrypts the existing data and the
   server then deletes the plain-text copy, including app names no other account uses.
3. Update the apps and the Linux agent (`onyx-agent unlock`). Old app versions can't upload to an account with
   encryption (the server answers 409); they keep their data until updated (Android keeps about a week of history,
   the Linux agent its queue).

What the server can't do any more: weekly report charts and website filtering happen on the devices, and a
forgotten passphrase can't be reset by the operator.

## See also

- [Self-Hosting](Self-Hosting.md)
- [Linux Agent](Linux-Agent.md)
- [Android App](Android-App.md)
- [Websites](Websites.md)
- [Developer Documentation](Developer-Documentation.md)
- [Overview of all articles](General.md)
