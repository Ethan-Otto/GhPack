#!/bin/sh
# Builds the plugin and makes dist/ghpack-<version>-rh8_0-any.yak.
# Needs the .NET SDK and the yak CLI (it ships with Rhino). Set YAK to its path if it is not found.
set -eu
cd "$(dirname "$0")"

if [ -z "${YAK:-}" ]; then
  for candidate in \
    "/Applications/Rhino 9.app/Contents/Resources/bin/yak" \
    "/Applications/Rhino 8.app/Contents/Resources/bin/yak" \
    "/c/Program Files/Rhino 9/System/Yak.exe" \
    "/c/Program Files/Rhino 8/System/Yak.exe"; do
    if [ -x "$candidate" ]; then YAK="$candidate"; break; fi
  done
fi
[ -n "${YAK:-}" ] || { echo "yak not found. Set YAK=/path/to/yak" >&2; exit 1; }

dotnet build src/GhPack -c Release
rm -rf dist && mkdir dist
cp src/GhPack/bin/Release/net7.0/GhPack.rhp src/GhPack/bin/Release/net7.0/GhPack.Core.dll \
   src/GhPack/bin/Release/net7.0/GhPack.rui package/manifest.yml package/icon.png dist/
(cd dist && "$YAK" build --platform any)
ls dist/*.yak
