#!/usr/bin/env bash
set -euo pipefail

version="${1:?usage: build-deb.sh <version> [output-dir]}"
out_dir="${2:-dist/native-linux}"
root="$(cd "$(dirname "$0")/../.." && pwd)"
work="$root/build/native-linux"
publish="$work/publish"
pkg="$work/pkg"

rm -rf "$work" "$root/$out_dir"
mkdir -p "$publish" "$pkg/DEBIAN" "$pkg/opt/movebit" \
  "$pkg/usr/bin" "$pkg/usr/share/applications" \
  "$pkg/usr/share/icons/hicolor/256x256/apps" "$root/$out_dir"

cd "$root"
dotnet publish MoveBit.csproj -c Release -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:PublishTrimmed=false \
  -o "$publish"

find "$publish" -name '*.pdb' -delete
cp -R "$publish"/. "$pkg/opt/movebit/"
chmod +x "$pkg/opt/movebit/MoveBit"

cat > "$pkg/DEBIAN/control" <<EOF
Package: movebit
Version: ${version}
Section: utils
Priority: optional
Architecture: amd64
Maintainer: turinglambdaai
Homepage: https://movebit.jrtx.site/
Depends: libx11-6, libxss1, libxkbcommon0, libfontconfig1
Suggests: dbus, libglib2.0-bin
Description: Healthy work rhythm companion
 MoveBit tracks active work time and provides sit, water, and micro-break reminders.
EOF

cat > "$pkg/usr/share/applications/movebit.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=MoveBit
Comment=Healthy work rhythm companion
Exec=/opt/movebit/MoveBit
Icon=movebit
Terminal=false
Categories=Utility;
StartupNotify=false
EOF

cp "$root/Assets/icon.png" "$pkg/usr/share/icons/hicolor/256x256/apps/movebit.png"
ln -s /opt/movebit/MoveBit "$pkg/usr/bin/movebit"

artifact="$root/$out_dir/MoveBit-linux-x64.deb"
dpkg-deb --build --root-owner-group "$pkg" "$artifact"
dpkg-deb --info "$artifact" >/dev/null
contents="$work/deb-contents.txt"
dpkg-deb --contents "$artifact" > "$contents"
grep -q './opt/movebit/MoveBit' "$contents"
grep -q './usr/share/applications/movebit.desktop' "$contents"

hash="$(sha256sum "$artifact" | awk '{print $1}')"
printf '%s  %s' "$hash" "$(basename "$artifact")" > "$artifact.sha256"
