#!/usr/bin/env bash
# Builds a signed Onyx APK for installing on your own phone (sideloading), optionally pointed at your server.
#
#   packaging/android/build-apk.sh --server https://onyx.example.com
#   packaging/android/build-apk.sh --server http://10.8.0.1:8080 --private-network --install
#
#   --server URL        Server a fresh install starts with (can still be changed on the login page).
#   --private-network   Also allow http:// servers and user-installed CA certificates (VPN/LAN-only servers).
#   --install           Install on the phone connected over USB/Wi-Fi debugging (adb).
#
# The signing key is created on first use in ~/.config/onyx-build/ (override with ONYX_KEYSTORE). Keep it: an
# update must be signed with the same key, otherwise Android makes you uninstall the old app first.
# Passwords are read from ONYX_KEYSTORE_PASSWORD or prompted.
# SDK locations default to what InstallAndroidDependencies sets up: ~/Android/Sdk and ~/Android/jdk.
set -euo pipefail

repo="$(cd "$(dirname "$0")/../.." && pwd)"
server=""
private_network=false
install=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --server) server="$2"; shift 2 ;;
        --private-network) private_network=true; shift ;;
        --install) install=true; shift ;;
        -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

sdk="${ANDROID_HOME:-$HOME/Android/Sdk}"
jdk="${JAVA_HOME_ANDROID:-$HOME/Android/jdk}"
keystore="${ONYX_KEYSTORE:-$HOME/.config/onyx-build/onyx-sideload.keystore}"
alias_name="onyx"

if [[ ! -d "$sdk" ]]; then
    echo "No Android SDK at $sdk. Install it with:" >&2
    echo "  dotnet build src/Onyx.App/Onyx.App/Onyx.App.csproj -f net10.0-android -t:InstallAndroidDependencies \\" >&2
    echo "    -p:AndroidSdkDirectory=\$HOME/Android/Sdk -p:JavaSdkDirectory=\$HOME/Android/jdk -p:AcceptAndroidSdkLicenses=True" >&2
    exit 1
fi

if [[ -z "${ONYX_KEYSTORE_PASSWORD:-}" ]]; then
    read -rsp "Keystore password (min. 6 characters): " ONYX_KEYSTORE_PASSWORD; echo
fi
export ONYX_KEYSTORE_PASSWORD

if [[ ! -f "$keystore" ]]; then
    echo "Creating signing key $keystore"
    mkdir -p "$(dirname "$keystore")"
    "$jdk/bin/keytool" -genkeypair -keystore "$keystore" -alias "$alias_name" -keyalg RSA -keysize 4096 \
        -validity 10000 -dname "CN=Onyx self-hosted" \
        -storepass:env ONYX_KEYSTORE_PASSWORD -keypass:env ONYX_KEYSTORE_PASSWORD
    chmod 600 "$keystore"
fi

# Must grow with every build so Android accepts it as an update: minutes since 2024-01-01.
version_code=$(( ($(date +%s) - 1704067200) / 60 ))

args=(
    -f net10.0-android -c Release -o "$repo/out/android"
    -p:AndroidSdkDirectory="$sdk" -p:JavaSdkDirectory="$jdk"
    -p:ApplicationVersion="$version_code"
    -p:AndroidPackageFormats=apk
    -p:AndroidKeyStore=true
    -p:AndroidSigningKeyStore="$keystore"
    -p:AndroidSigningStorePass=env:ONYX_KEYSTORE_PASSWORD
    -p:AndroidSigningKeyAlias="$alias_name"
    -p:AndroidSigningKeyPass=env:ONYX_KEYSTORE_PASSWORD
)
[[ -n "$server" ]] && args+=(-p:OnyxServerUrl="$server")
$private_network && args+=(-p:OnyxPrivateNetwork=true)

rm -rf "$repo/out/android"
dotnet publish "$repo/src/Onyx.App/Onyx.App/Onyx.App.csproj" "${args[@]}"

apk="$(ls "$repo"/out/android/*-Signed.apk | head -n 1)"
cp "$apk" "$repo/out/onyx-android.apk"
echo "APK: $repo/out/onyx-android.apk"

if $install; then
    "$sdk/platform-tools/adb" install -r "$repo/out/onyx-android.apk"
fi
