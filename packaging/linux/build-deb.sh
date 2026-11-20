#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-2.7.1}"
VERSION="${VERSION#v}"

GUI_DIR="${2:-publish/gui/linux-x64}"
CLI_DIR="${3:-publish/cli/linux-x64}"
OUTPUT_DIR="${4:-publish/installer}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

PKG_DIR=$(mktemp -d -t photocropper-deb-XXXXXX)
# mktemp -d makes the folder 0700; packaging tools (appimagetool) refuse an AppDir others can't read,
# and the package's root would carry that mode.
chmod 755 "${PKG_DIR}"
trap 'rm -rf "${PKG_DIR}"' EXIT

mkdir -p "${PKG_DIR}/DEBIAN"
mkdir -p "${PKG_DIR}/usr/bin"
mkdir -p "${PKG_DIR}/usr/lib/photocropper"
mkdir -p "${PKG_DIR}/usr/share/applications"
mkdir -p "${PKG_DIR}/usr/share/icons/hicolor/512x512/apps"
mkdir -p "${PKG_DIR}/usr/share/metainfo"
mkdir -p "${PKG_DIR}/usr/share/doc/photocropper"

# Copy binaries
cp -r "${REPO_ROOT}/${GUI_DIR}/"* "${PKG_DIR}/usr/lib/photocropper/"
if [[ -f "${REPO_ROOT}/${CLI_DIR}/PhotoCropper.Cli" ]]; then
  cp "${REPO_ROOT}/${CLI_DIR}/PhotoCropper.Cli" "${PKG_DIR}/usr/lib/photocropper/"
fi

# Ensure executable permissions
chmod +x "${PKG_DIR}/usr/lib/photocropper/PhotoCropper.Gui"
if [[ -f "${PKG_DIR}/usr/lib/photocropper/PhotoCropper.Cli" ]]; then
  chmod +x "${PKG_DIR}/usr/lib/photocropper/PhotoCropper.Cli"
fi

# Launchers in /usr/bin. .NET aborts at startup without ICU (libicu), and Ubuntu renames its package every release
# (libicu74 in 24.04, libicu78 in 26.04, ...), so the package only recommends it; where it's missing, the launcher
# runs the app without culture data (numbers and dates formatted invariantly), as the AppImage's AppRun does.
write_launcher() { # launcher path, program
  cat > "$1" <<EOF
#!/bin/sh
if ! /sbin/ldconfig -p 2> /dev/null | grep -q 'libicuuc\.so'; then
  export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
fi
exec "$2" "\$@"
EOF
  chmod 755 "$1"
}
write_launcher "${PKG_DIR}/usr/bin/photocropper" /usr/lib/photocropper/PhotoCropper.Gui
ln -sf photocropper "${PKG_DIR}/usr/bin/PhotoCropper"
if [[ -f "${PKG_DIR}/usr/lib/photocropper/PhotoCropper.Cli" ]]; then
  write_launcher "${PKG_DIR}/usr/bin/photocropper-cli" /usr/lib/photocropper/PhotoCropper.Cli
  ln -sf photocropper-cli "${PKG_DIR}/usr/bin/PhotoCropper.Cli"
fi

# Desktop & icons & metainfo
cp "${SCRIPT_DIR}/PhotoCropper.desktop" "${PKG_DIR}/usr/share/applications/"
cp "${REPO_ROOT}/src/PhotoCropper.Gui/Assets/app_icon.png" "${PKG_DIR}/usr/share/icons/hicolor/512x512/apps/photocropper.png"
cp "${SCRIPT_DIR}/io.github.atm0n.photocropper.metainfo.xml" "${PKG_DIR}/usr/share/metainfo/"
cp "${REPO_ROOT}/LICENSE" "${PKG_DIR}/usr/share/doc/photocropper/copyright"

# Calculate installed size in KB
INSTALLED_SIZE=$(du -sk "${PKG_DIR}" | cut -f1)

# Control file
cat <<EOF > "${PKG_DIR}/DEBIAN/control"
Package: photocropper
Version: ${VERSION}
Section: graphics
Priority: optional
Architecture: amd64
Installed-Size: ${INSTALLED_SIZE}
Maintainer: Atm0n <https://github.com/Atm0n/PhotoCropper>
Depends: libgl1, libglib2.0-0 | libglib2.0-0t64, libx11-6, libfontconfig1
Recommends: libsane1, sane-utils, libicu80 | libicu79 | libicu78 | libicu77 | libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu67 | libicu66
Homepage: https://github.com/Atm0n/PhotoCropper
Description: Batch photo cropper and scanner image extractor
 PhotoCropper automatically detects, crops, deskews, and restores multiple photos
 scanned together on flatbed scanners. Includes high-throughput CLI batch processor.
EOF

mkdir -p "${REPO_ROOT}/${OUTPUT_DIR}"
DEB_NAME="PhotoCropper-${VERSION}-linux-x64.deb"
dpkg-deb --build --root-owner-group "${PKG_DIR}" "${REPO_ROOT}/${OUTPUT_DIR}/${DEB_NAME}"
echo "Successfully created Debian package: ${OUTPUT_DIR}/${DEB_NAME}"
