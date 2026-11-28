# Integrations

Other programs can read your numbers: an n8n workflow, a script, a spreadsheet import or an AI assistant. Because
everything is end-to-end encrypted, the server can't answer such requests itself. Instead, each program gets a
**key** of its own, and your devices keep a snapshot of what you allow encrypted for that key. The program
downloads the snapshot from the server and decrypts it with the key.

## Creating a key

**Settings → Integrations → Add integration.** Choose a name, how many days of history (7, 30, 90 or 365) and what
the program may see:

| Always | Optional |
|---|---|
| Screen time per day, app opens, device unlocks | Apps (time and opens), categories, websites (time and visits), device names |

**Create key** shows the key, `onyx1.<id>.<secret>`. Copy it into the program together with your server's address.
Treat it like a password: anyone with it can read what the snapshot contains (but nothing else, and they can't
change anything). **Delete** stops the key working immediately. **Edit** changes the scope; the next snapshot
follows it.

The snapshot is updated when you open the dashboard and it is more than an hour old, and with **Update now**. A
program therefore sees your numbers as of the last time you used the dashboard on any device.

## Reading the snapshot

1. Split the key at the dots: `onyx1`, the **id** and the **secret** (base64url, 32 bytes).
2. Derive two values with HMAC-SHA256 keyed with the secret:
   - the **access token**: `base64url(HMAC(secret, "onyx-integration|auth"))`
   - the **AES key**: `HMAC(secret, "onyx-integration|key")` (32 bytes)
3. `GET <server>/api/integration/data` with the header `Authorization: Bearer <id>.<access token>`. The answer
   is `{"id": "...", "updatedAt": "2026-11-28T09:20:53", "data": "<base64>"}`; `data` is null until a device has
   published a snapshot.
4. Decrypt `data` with AES-256-GCM: byte 0 is the format version (1), bytes 1–12 the nonce, the last 16 bytes
   the tag, everything in between the ciphertext. The associated data is `integration|<id>` (UTF-8).

The endpoint allows 60 requests per minute per IP address. A wrong or deleted key gets `401`.

### Python

Needs the `cryptography` package (`pip install cryptography`).

```python
import base64, hashlib, hmac, json, urllib.request
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

def b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()

def read_onyx(server: str, key: str) -> dict:
    prefix, key_id, secret = key.strip().split(".")
    assert prefix == "onyx1", "not an Onyx integration key"
    secret = base64.urlsafe_b64decode(secret + "=" * (-len(secret) % 4))
    token = b64url(hmac.new(secret, b"onyx-integration|auth", hashlib.sha256).digest())
    aes_key = hmac.new(secret, b"onyx-integration|key", hashlib.sha256).digest()

    request = urllib.request.Request(server.rstrip("/") + "/api/integration/data",
                                     headers={"Authorization": f"Bearer {key_id}.{token}"})
    with urllib.request.urlopen(request) as response:
        export = json.load(response)
    if export["data"] is None:
        raise RuntimeError("No snapshot yet: open the Onyx dashboard once")

    blob = base64.b64decode(export["data"])
    plain = AESGCM(aes_key).decrypt(blob[1:13], blob[13:], f"integration|{key_id}".encode())
    return json.loads(plain)

print(json.dumps(read_onyx("https://onyx.example.com", "onyx1.…"), indent=2))
```

### JavaScript (Node, n8n)

In n8n, fetch the snapshot with an **HTTP Request** node (the header from step 3; compute the token once with the
first lines below) and decrypt it in a **Code** node. Self-hosted n8n only allows `crypto` in Code nodes with
`NODE_FUNCTION_ALLOW_BUILTIN=crypto`.

```javascript
const crypto = require('crypto');

const key = 'onyx1.…';
const [, id, secretText] = key.trim().split('.');
const secret = Buffer.from(secretText, 'base64url');
const token = crypto.createHmac('sha256', secret).update('onyx-integration|auth').digest('base64url');
const aesKey = crypto.createHmac('sha256', secret).update('onyx-integration|key').digest();
// Authorization: `Bearer ${id}.${token}`

const blob = Buffer.from($json.data, 'base64');
const decipher = crypto.createDecipheriv('aes-256-gcm', aesKey, blob.subarray(1, 13));
decipher.setAAD(Buffer.from(`integration|${id}`));
decipher.setAuthTag(blob.subarray(blob.length - 16));
const plain = Buffer.concat([decipher.update(blob.subarray(13, blob.length - 16)), decipher.final()]);
return [{ json: JSON.parse(plain.toString('utf8')) }];
```

### AI assistants

Give the assistant the Python function above as a tool (or let it run the script) with the server and key from
its configuration, not from the chat. It then answers questions such as "how much time did I spend on social
apps this week compared with last week" from the snapshot.

### Test vector

For the key `onyx1.AAAAAAAAAAAAAAAAAAAAAA.AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8` (secret: the bytes 0 to 31)
the access token is `jeE-wtBjgzR3jyACy-m8m2BgExaZ78J4QOde3CXd_0s` and the AES key (hex)
`64d537f4cfb6a99155f9667d5f554b17e86fa8b493a4c6243faf9a7b06ff1748`.

## Snapshot format

camelCase JSON. Days are in the time zone of the device that published, oldest first, today last (as far as
recorded). Minutes have one decimal. Lists the key may not read are left out.

```json
{
  "format": 1,
  "generatedAt": "2026-11-28T09:20:53+00:00",
  "timeZone": "Europe/Vienna",
  "from": "2026-10-30",
  "to": "2026-11-28",
  "days": [
    {
      "date": "2026-11-28",
      "screenTimeMinutes": 142.5,
      "appOpens": 61,
      "unlocks": 38,
      "apps": [{ "name": "Firefox", "category": "Productivity", "minutes": 80.2, "opens": 9 }],
      "categories": [{ "name": "Productivity", "minutes": 95.0 }],
      "websites": [{ "domain": "github.com", "minutes": 31.4, "visits": 12 }],
      "devices": [{ "name": "Pixel 8", "minutes": 62.3 }]
    }
  ]
}
```

`format` changes only when a field changes meaning or disappears; new fields may appear in format 1.

## What the server sees

The server stores the SHA-256 of the access token (not the token), the encrypted snapshot, when it was published
and when the integration last read it. It never gets the secret or the AES key: the secret lives in your encrypted
settings, so every device of yours can publish. Deleting the account deletes all integrations.

## See also

- [End-to-End Encryption](Encryption.md)
- [Overview of all articles](General.md)
