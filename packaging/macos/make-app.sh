#!/usr/bin/env bash
# The Mac's version (Apple Silicon) for a test: SanctuaryVault.app, zipped, not signed by Apple (the binaries carry .NET's,
# Avalonia's and the libraries' own signatures). The releases make theirs with Velopack (.github/workflows/release.yml),
# which updates itself. Runs on Linux: dotnet, curl, shasum, 7z and zip.
#   packaging/macos/make-app.sh <version> <output folder>
set -euo pipefail

version=${1:?version}
out=$(realpath -m "${2:?output folder}")
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

app="$work/SanctuaryVault.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
dotnet publish "$repo/gamevault/gamevault.csproj" -c Release -r osx-arm64 --self-contained true -p:Version="$version" -o "$app/Contents/MacOS"
bash "$here/bundle-tools.sh" "$app/Contents/MacOS"

sed "s/VERSION/$version/g" "$here/Info.plist" > "$app/Contents/Info.plist"
cp "$here/SanctuaryVault.icns" "$app/Contents/Resources/"
chmod +x "$app/Contents/MacOS/gamevault"

mkdir -p "$out"
rm -f "$out/SanctuaryVault-$version-macOS.zip"
(cd "$work" && zip -qry "$out/SanctuaryVault-$version-macOS.zip" SanctuaryVault.app)
echo "$out/SanctuaryVault-$version-macOS.zip"
