#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-2.6.0}"
VERSION="${VERSION#v}"

GUI_DIR="${2:-publish/gui/linux-x64}"
CLI_DIR="${3:-publish/cli/linux-x64}"
OUTPUT_DIR="${4:-publish/installer}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

APPDIR=$(mktemp -d -t photocropper-appdir-XXXXXX)
trap 'rm -rf "${APPDIR}"' EXIT

mkdir -p "${APPDIR}/usr/bin"
mkdir -p "${APPDIR}/usr/share/applications"
mkdir -p "${APPDIR}/usr/share/icons/hicolor/512x512/apps"
mkdir -p "${APPDIR}/usr/share/metainfo"

# Copy binaries into usr/bin
cp -r "${REPO_ROOT}/${GUI_DIR}/"* "${APPDIR}/usr/bin/"
chmod +x "${APPDIR}/usr/bin/PhotoCropper.Gui"

if [[ -f "${REPO_ROOT}/${CLI_DIR}/PhotoCropper.Cli" ]]; then
  cp "${REPO_ROOT}/${CLI_DIR}/PhotoCropper.Cli" "${APPDIR}/usr/bin/"
  chmod +x "${APPDIR}/usr/bin/PhotoCropper.Cli"
  ln -sf "PhotoCropper.Cli" "${APPDIR}/usr/bin/photocropper-cli"
fi

ln -sf "PhotoCropper.Gui" "${APPDIR}/usr/bin/photocropper"
ln -sf "PhotoCropper.Gui" "${APPDIR}/usr/bin/PhotoCropper"

# Copy desktop file and icons into standard locations and AppDir root
cp "${SCRIPT_DIR}/PhotoCropper.desktop" "${APPDIR}/"
cp "${SCRIPT_DIR}/PhotoCropper.desktop" "${APPDIR}/usr/share/applications/"

cp "${REPO_ROOT}/src/PhotoCropper.Gui/Assets/app_icon.png" "${APPDIR}/photocropper.png"
cp "${REPO_ROOT}/src/PhotoCropper.Gui/Assets/app_icon.png" "${APPDIR}/.DirIcon"
cp "${REPO_ROOT}/src/PhotoCropper.Gui/Assets/app_icon.png" "${APPDIR}/usr/share/icons/hicolor/512x512/apps/photocropper.png"

cp "${SCRIPT_DIR}/io.github.atm0n.photocropper.metainfo.xml" "${APPDIR}/usr/share/metainfo/"

# Create custom AppRun script
cat <<'EOF' > "${APPDIR}/AppRun"
#!/usr/bin/env bash
set -e
HERE="$(dirname "$(readlink -f "${0}")")"
export PATH="${HERE}/usr/bin:${PATH}"
export LD_LIBRARY_PATH="${HERE}/usr/bin:${LD_LIBRARY_PATH:-}"

INVOKED_NAME="$(basename "${0}")"
if [[ "$INVOKED_NAME" == "photocropper-cli" || "$INVOKED_NAME" == "PhotoCropper.Cli" ]]; then
  exec "${HERE}/usr/bin/PhotoCropper.Cli" "$@"
fi

exec "${HERE}/usr/bin/PhotoCropper.Gui" "$@"
EOF
chmod +x "${APPDIR}/AppRun"

# Download and extract appimagetool if not installed locally
if ! command -v appimagetool >/dev/null 2>&1; then
  TOOL_DIR=$(mktemp -d -t appimagetool-XXXXXX)
  echo "Downloading appimagetool..."
  curl --proto '=https' --proto-redir '=https' -sSfL "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage" -o "${TOOL_DIR}/appimagetool"
  chmod +x "${TOOL_DIR}/appimagetool"
  cd "${TOOL_DIR}"
  ./appimagetool --appimage-extract >/dev/null 2>&1
  APPIMAGETOOL="${TOOL_DIR}/squashfs-root/AppRun"
  cd "${REPO_ROOT}"
else
  APPIMAGETOOL="appimagetool"
fi

mkdir -p "${REPO_ROOT}/${OUTPUT_DIR}"
APPIMAGE_NAME="PhotoCropper-${VERSION}-x86_64.AppImage"

echo "Building AppImage..."
ARCH=x86_64 "$APPIMAGETOOL" "${APPDIR}" "${REPO_ROOT}/${OUTPUT_DIR}/${APPIMAGE_NAME}"

echo "Successfully created AppImage: ${OUTPUT_DIR}/${APPIMAGE_NAME}"
