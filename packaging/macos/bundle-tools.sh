#!/usr/bin/env bash
# What the Mac's app brings beside the client (in its Contents/MacOS folder): 7-Zip (the games in several files arrive
# zipped) and SDL2 (Régler mes touches reads the gamepad with it: the Mac's RetroArch has its own inside it, not one the
# client can load). On Linux or a Mac: curl, shasum, tar, and 7z or hdiutil for SDL2's disk image.
#   packaging/macos/bundle-tools.sh <the published client's folder>
set -euo pipefail

target=${1:?the folder of the published client}
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

fetch() { # url, sha256, file
  curl -sfL -o "$3" "$1"
  echo "$2  $3" | shasum -a 256 -c --quiet
}

fetch https://www.7-zip.org/a/7z2501-mac.tar.xz 26aa75bc262bb10bf0805617b95569c3035c2c590a99f7db55c7e9607b2685e0 "$work/7z.tar.xz"
mkdir -p "$target/Lib/7z"
tar -xJf "$work/7z.tar.xz" -C "$target/Lib/7z" 7zz License.txt
chmod +x "$target/Lib/7z/7zz"

fetch https://github.com/libsdl-org/SDL/releases/download/release-2.32.10/SDL2-2.32.10.dmg 4a7ac31640d70214e848f994be8a12849c0f97918a7e6c2e27a40036166d1a7f "$work/SDL2.dmg"
if command -v hdiutil > /dev/null; then
  hdiutil attach -nobrowse -readonly -mountpoint "$work/sdl" "$work/SDL2.dmg" > /dev/null
  cp "$work/sdl/SDL2.framework/Versions/A/SDL2" "$target/libSDL2.dylib"
  hdiutil detach "$work/sdl" > /dev/null
else
  "$(command -v 7zz || command -v 7z)" e -y -o"$work/sdl" "$work/SDL2.dmg" "SDL2/SDL2.framework/Versions/A/SDL2" > /dev/null
  mv "$work/sdl/SDL2" "$target/libSDL2.dylib"
fi
