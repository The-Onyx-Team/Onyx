// Browser helpers for Onyx: WebCrypto for the end-to-end encryption (same byte layout as DotNetCrypto.cs),
// storage, downloads and image scaling. Loaded as a module by OnyxJs.cs.

const encoder = new TextEncoder();
const VERSION = 1, NONCE = 12;

const fromBase64 = s => Uint8Array.from(atob(s), c => c.charCodeAt(0));
const toBase64 = bytes => {
    let binary = "";
    for (let i = 0; i < bytes.length; i += 0x8000)
        binary += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
    return btoa(binary);
};
const fromBase64Url = s => fromBase64(s.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(s.length / 4) * 4, "="));
const toBase64Url = bytes => toBase64(bytes).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");

function subtle() {
    if (!globalThis.crypto || !crypto.subtle)
        throw new Error("This page needs HTTPS: the browser only offers encryption on secure connections.");
    return crypto.subtle;
}

const aesKey = key => subtle().importKey("raw", key, "AES-GCM", false, ["encrypt", "decrypt"]);

async function encryptWith(key, plaintext, context) {
    const nonce = crypto.getRandomValues(new Uint8Array(NONCE));
    const sealed = new Uint8Array(await subtle().encrypt(
        { name: "AES-GCM", iv: nonce, additionalData: encoder.encode(context), tagLength: 128 }, key, plaintext));
    const blob = new Uint8Array(1 + NONCE + sealed.length);
    blob[0] = VERSION;
    blob.set(nonce, 1);
    blob.set(sealed, 1 + NONCE);
    return blob;
}

async function decryptWith(key, blob, context) {
    if (blob.length < 1 + NONCE + 16 || blob[0] !== VERSION)
        throw new Error("Not an Onyx encrypted blob");
    return new Uint8Array(await subtle().decrypt(
        { name: "AES-GCM", iv: blob.subarray(1, 1 + NONCE), additionalData: encoder.encode(context), tagLength: 128 },
        key, blob.subarray(1 + NONCE)));
}

export async function deriveKey(passphrase, salt, iterations) {
    const base = await subtle().importKey("raw", encoder.encode(passphrase), "PBKDF2", false, ["deriveBits"]);
    return new Uint8Array(await subtle().deriveBits({ name: "PBKDF2", hash: "SHA-256", salt, iterations }, base, 256));
}

export async function hmac(key, data) {
    const k = await subtle().importKey("raw", key, { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
    return new Uint8Array(await subtle().sign("HMAC", k, data));
}

export async function encrypt(key, plaintext, context) {
    return encryptWith(await aesKey(key), plaintext, context);
}

export async function decrypt(key, blob, context) {
    return decryptWith(await aesKey(key), blob, context);
}

// Batches: items and results are base64, one interop call for thousands of rows.
export async function encryptMany(key, items) {
    const k = await aesKey(key);
    return Promise.all(items.map(async i => toBase64(await encryptWith(k, fromBase64(i.data), i.context))));
}

export async function decryptMany(key, items) {
    const k = await aesKey(key);
    return Promise.all(items.map(async i => {
        try {
            return toBase64(await decryptWith(k, fromBase64(i.data), i.context));
        } catch {
            return null;
        }
    }));
}

export async function createKeyPair() {
    const pair = await subtle().generateKey({ name: "ECDH", namedCurve: "P-256" }, true, ["deriveBits"]);
    const jwk = await subtle().exportKey("jwk", pair.privateKey);
    const q = new Uint8Array(65);
    q[0] = 4;
    q.set(fromBase64Url(jwk.x), 1);
    q.set(fromBase64Url(jwk.y), 33);
    return { privateKey: toBase64(fromBase64Url(jwk.d)), publicKey: toBase64(q) };
}

export async function sharedKey(privateKey, publicKey, otherPublicKey) {
    const mine = await subtle().importKey("jwk", {
        kty: "EC", crv: "P-256", ext: true,
        d: toBase64Url(privateKey), x: toBase64Url(publicKey.subarray(1, 33)), y: toBase64Url(publicKey.subarray(33, 65))
    }, { name: "ECDH", namedCurve: "P-256" }, false, ["deriveBits"]);
    const other = await subtle().importKey("raw", otherPublicKey, { name: "ECDH", namedCurve: "P-256" }, false, []);
    const secret = await subtle().deriveBits({ name: "ECDH", public: other }, mine, 256);
    return new Uint8Array(await subtle().digest("SHA-256", secret));
}

// Storage that may be unavailable (private windows, blocked site data): failures read as "nothing stored".
function area(name) {
    try {
        return name === "session" ? sessionStorage : localStorage;
    } catch {
        return null;
    }
}

export function storageGet(name, key) {
    try { return area(name)?.getItem(key) ?? null; } catch { return null; }
}

export function storageSet(name, key, value) {
    try { area(name)?.setItem(key, value); } catch { /* not available */ }
}

export function storageRemove(name, key) {
    try { area(name)?.removeItem(key); } catch { /* not available */ }
}

export function download(name, type, bytes) {
    const url = URL.createObjectURL(new Blob([bytes], { type }));
    const a = document.createElement("a");
    a.href = url;
    a.download = name;
    document.body.appendChild(a);
    a.click();
    setTimeout(() => { URL.revokeObjectURL(url); a.remove(); }, 2000);
}

export function openInNewTab(type, bytes) {
    const url = URL.createObjectURL(new Blob([bytes], { type }));
    window.open(url, "_blank", "noopener");
    setTimeout(() => URL.revokeObjectURL(url), 60000);
}

// Scales a picture to fit size × size and returns it as PNG (transparent background kept).
export async function resizeImage(bytes, size) {
    const isSvg = bytes.length > 4 && (bytes[0] === 0x3C || (bytes[0] === 0xEF && bytes[3] === 0x3C));
    const url = URL.createObjectURL(new Blob([bytes], isSvg ? { type: "image/svg+xml" } : {}));
    try {
        const img = new Image();
        img.src = url;
        await img.decode();
        const width = img.naturalWidth || size, height = img.naturalHeight || size;
        const scale = Math.min(size / width, size / height, isSvg ? Infinity : 1);
        const canvas = document.createElement("canvas");
        canvas.width = Math.max(1, Math.round(width * scale));
        canvas.height = Math.max(1, Math.round(height * scale));
        const context = canvas.getContext("2d");
        context.imageSmoothingQuality = "high";
        context.drawImage(img, 0, 0, canvas.width, canvas.height);
        const png = await new Promise(resolve => canvas.toBlob(resolve, "image/png"));
        return new Uint8Array(await png.arrayBuffer());
    } finally {
        URL.revokeObjectURL(url);
    }
}

export function getTimeZone() {
    return Intl.DateTimeFormat().resolvedOptions().timeZone;
}
