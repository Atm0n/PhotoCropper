#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-2.5.2}"
VERSION="${VERSION#v}"

GUI_DIR="${2:-publish/gui/linux-x64}"
CLI_DIR="${3:-publish/cli/linux-x64}"
OUTPUT_DIR="${4:-publish/installer}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

PKG_DIR=$(mktemp -d -t photocropper-deb-XXXXXX)
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

# Symlinks in /usr/bin
ln -sf "/usr/lib/photocropper/PhotoCropper.Gui" "${PKG_DIR}/usr/bin/photocropper"
ln -sf "/usr/lib/photocropper/PhotoCropper.Gui" "${PKG_DIR}/usr/bin/PhotoCropper"
if [[ -f "${PKG_DIR}/usr/lib/photocropper/PhotoCropper.Cli" ]]; then
  ln -sf "/usr/lib/photocropper/PhotoCropper.Cli" "${PKG_DIR}/usr/bin/photocropper-cli"
  ln -sf "/usr/lib/photocropper/PhotoCropper.Cli" "${PKG_DIR}/usr/bin/PhotoCropper.Cli"
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
Depends: libgomp1, libgl1, libglib2.0-0, libx11-6
Recommends: libsane1, sane-utils
Homepage: https://github.com/Atm0n/PhotoCropper
Description: Batch photo cropper and scanner image extractor
 PhotoCropper automatically detects, crops, deskews, and restores multiple photos
 scanned together on flatbed scanners. Includes high-throughput CLI batch processor.
EOF

mkdir -p "${REPO_ROOT}/${OUTPUT_DIR}"
DEB_NAME="PhotoCropper-${VERSION}-linux-x64.deb"
dpkg-deb --build --root-owner-group "${PKG_DIR}" "${REPO_ROOT}/${OUTPUT_DIR}/${DEB_NAME}"
echo "Successfully created Debian package: ${OUTPUT_DIR}/${DEB_NAME}"
