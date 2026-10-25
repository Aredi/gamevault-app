#!/usr/bin/env bash
# The Mac's version (Apple Silicon): SanctuaryVault.app, zipped, not signed by Apple (the binaries carry .NET's,
# Avalonia's and the libraries' own signatures). Runs on Linux, like the other release jobs: dotnet, curl, 7z and zip.
#   packaging/macos/make-app.sh <version> <output folder>
# The app comes with 7-Zip (the games in several files arrive zipped) and SDL2 (Régler mes touches reads the gamepad
# with it: the Mac's RetroArch has its own inside it, not one the client can load).
set -euo pipefail

version=${1:?version}
out=$(realpath -m "${2:?output folder}")
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

sevenzip=$(command -v 7zz || command -v 7z)
fetch() { # url, sha256, file
  curl -sfL -o "$3" "$1"
  echo "$2  $3" | sha256sum -c --quiet
}

app="$work/SanctuaryVault.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
dotnet publish "$repo/gamevault/gamevault.csproj" -c Release -r osx-arm64 --self-contained true -p:Version="$version" -o "$app/Contents/MacOS"

fetch https://www.7-zip.org/a/7z2501-mac.tar.xz 26aa75bc262bb10bf0805617b95569c3035c2c590a99f7db55c7e9607b2685e0 "$work/7z.tar.xz"
mkdir -p "$app/Contents/MacOS/Lib/7z"
tar -xJf "$work/7z.tar.xz" -C "$app/Contents/MacOS/Lib/7z" 7zz License.txt

fetch https://github.com/libsdl-org/SDL/releases/download/release-2.32.10/SDL2-2.32.10.dmg 4a7ac31640d70214e848f994be8a12849c0f97918a7e6c2e27a40036166d1a7f "$work/SDL2.dmg"
"$sevenzip" e -y -o"$work/sdl" "$work/SDL2.dmg" "SDL2/SDL2.framework/Versions/A/SDL2" > /dev/null
mv "$work/sdl/SDL2" "$app/Contents/MacOS/libSDL2.dylib"

sed "s/VERSION/$version/g" "$here/Info.plist" > "$app/Contents/Info.plist"
cp "$here/SanctuaryVault.icns" "$app/Contents/Resources/"
chmod +x "$app/Contents/MacOS/gamevault" "$app/Contents/MacOS/Lib/7z/7zz"

mkdir -p "$out"
rm -f "$out/SanctuaryVault-$version-macOS.zip"
(cd "$work" && zip -qry "$out/SanctuaryVault-$version-macOS.zip" SanctuaryVault.app)
echo "$out/SanctuaryVault-$version-macOS.zip"
