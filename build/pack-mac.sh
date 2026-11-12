#!/bin/bash
# Mac portable packages (run on the Linux dev box): publish osx-arm64 + osx-x64 self-contained, wrap each in the
# .app bundle, ad-hoc sign with rcodesign, verify, and zip with 使用说明.txt.
# Single-file publish: managed assemblies live inside the MeiliMac executable, so Contents/MacOS holds only Mach-O
# files (MeiliMac + native .dylib). Loose .dll files there are "nested code" to codesign and would need signatures in
# extended attributes, which a zip does not carry.
# usage: build/pack-mac.sh [out-dir]   (default: artifacts/mac)
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
out=${1:-$root/artifacts/mac}
rcodesign=${RCODESIGN:-rcodesign}
name="美丽稻谷卖方绿泡群专用终端"
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

mkdir -p "$out"
python3 "$root/build/mac/make_icon.py" "$work/AppIcon.icns"
cp "$root/build/mac/使用说明.txt" "$work/使用说明.txt"

for arch in arm64 x64; do
  pub="$work/pub-$arch"
  dotnet publish "$root/src/Meili.Mac" -c Release -r "osx-$arch" --self-contained true \
    -p:PublishTrimmed=false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false \
    -p:DebugType=none -p:GenerateDocumentationFile=false -o "$pub" -v q -nologo
  app="$work/$arch/$name.app"
  mkdir -p "$work/$arch"
  python3 "$root/build/mac/bundle.py" app "$pub" "$app" "$version" "$work/AppIcon.icns"
  # Apple silicon refuses to run unsigned arm64 code; ad-hoc signing seals every file in the bundle.
  "$rcodesign" sign "$app" >/dev/null
  python3 "$root/build/mac/verify_sig.py" "$app"
  if grep -rlaE "tyuStock|Users/tyu|Users\\\\tyu" "$app"; then echo "personal path leaked into the bundle" >&2; exit 1; fi
  zip="$out/$name-macOS-$arch.zip"
  [ "$arch" = arm64 ] && zip="$out/$name-macOS.zip"      # main package: Apple silicon
  python3 "$root/build/mac/bundle.py" zip "$zip" "$work/使用说明.txt" "$app"
  echo "$zip $(stat -c %s "$zip")"
done
cp "$work/使用说明.txt" "$out/使用说明.txt"
