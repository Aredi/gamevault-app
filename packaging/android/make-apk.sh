#!/usr/bin/env bash
# The Android app: SanctuaryVault-<version>-android.apk (arm64: phones, handhelds, TV boxes).
# Runs on Linux: dotnet (with the "android" workload) and the Android SDK (ANDROID_HOME, default ~/Android/Sdk).
# Signed with the key of ANDROID_KEYSTORE (+ ANDROID_KEYSTORE_PASS, ANDROID_KEY_ALIAS) when set, else with the
# debug key: an APK signed with another key does not update the installed one (uninstall it first).
set -euo pipefail

version="${1:-1.40.0}"
repo="$(cd "$(dirname "$0")/../.." && pwd)"
out="$repo/packaging/android/out"
sdk="${ANDROID_HOME:-$HOME/Android/Sdk}"

signing=()
if [ -n "${ANDROID_KEYSTORE:-}" ]; then
  signing=(-p:AndroidKeyStore=true -p:AndroidSigningKeyStore="$ANDROID_KEYSTORE"
    -p:AndroidSigningKeyAlias="${ANDROID_KEY_ALIAS:-sanctuaryvault}"
    -p:AndroidSigningStorePass="env:ANDROID_KEYSTORE_PASS" -p:AndroidSigningKeyPass="env:ANDROID_KEYSTORE_PASS")
fi

rm -rf "$out"
# The version code must grow with each release: 1.40.3 -> 14003
IFS=. read -r major minor patch <<< "$version"
code=$(( major * 10000 + minor * 100 + ${patch:-0} ))
# A Release build makes the signed APK; one ABI (both at once ask restore for a desktop Mono runtime)
dotnet build "$repo/gamevault.Android/gamevault.Android.csproj" -c Release -f net10.0-android -r android-arm64 \
  -p:AndroidSdkDirectory="$sdk" -p:Version="$version" -p:ApplicationDisplayVersion="$version" -p:ApplicationVersion="$code" \
  "${signing[@]}"
mkdir -p "$out"
apk="$(ls "$repo"/gamevault.Android/bin/Release/net10.0-android/android-arm64/*-Signed.apk | head -1)"
cp "$apk" "$out/SanctuaryVault-$version-android.apk"
echo "$out/SanctuaryVault-$version-android.apk"
