#!/usr/bin/env bash
# Smoke-tests the Linux release packages the way a user gets them: each check runs in a fresh ubuntu:24.04
# container that has only what the package itself brings (the .deb's declared dependencies; nothing for the
# AppImage). It catches packaging mistakes the unit tests can't see, such as a native library missing from the
# published app or a dependency the .deb forgets to declare.
#
#   packaging/smoke/linux-smoke.sh <folder with the .deb and .AppImage> <sample scans folder>
#
# The sample scans come from tools/PhotoCropper.SampleScans; their expected.txt says how many photos to expect.
# Needs Docker. Every check runs even if an earlier one fails; the script exits 1 if any failed.
set -uo pipefail

# pwd -W gives C:/... paths under Git Bash on Windows, which Docker Desktop needs; elsewhere plain pwd.
PACKAGES="$(cd "${1:?packages folder}" && (pwd -W 2> /dev/null || pwd))"
SAMPLES="$(cd "${2:?sample scans folder}" && (pwd -W 2> /dev/null || pwd))"
export MSYS_NO_PATHCONV=1
IMAGE="ubuntu:24.04"
EXPECTED=$(awk '$1 == "total" { print $2 }' "${SAMPLES}/expected.txt")
DEB=$(find "${PACKAGES}" -maxdepth 1 -name '*.deb' | head -1)
APPIMAGE=$(find "${PACKAGES}" -maxdepth 1 -name '*.AppImage' | head -1)

failures=0
results=()

record() { # name, exit code
  if [[ "$2" -eq 0 ]]; then results+=("PASS  $1"); else results+=("FAIL  $1"); failures=$((failures + 1)); fi
}

# Runs a bash script in a fresh container with the packages and samples mounted read-only.
in_container() {
  docker run --rm -e EXPECTED="${EXPECTED}" -v "${PACKAGES}:/pkgs:ro" -v "${SAMPLES}:/samples:ro" "${IMAGE}" bash -c "$1"
}

# Runs the CLI on the samples and checks it wrote exactly the expected number of photos.
# --min-size 1: the CLI's own default ignores photos under 25% of the page; this checks packaging, not tuning.
CLI_CHECK='
  out=$(mktemp -d)
  "$CLI" /samples -o "$out" --min-size 1 > /tmp/cli.log 2>&1
  rc=$?
  found=$(find "$out" -name "*.jpg" | wc -l)
  echo "CLI exit code $rc, $found of $EXPECTED photos"
  if [ "$rc" -ne 0 ] || [ "$found" -ne "$EXPECTED" ]; then tail -25 /tmp/cli.log; exit 1; fi'

# Starts the GUI on a virtual display; still running after 20 s counts as started (timeout exits 124).
# xvfb is test tooling, not something the package should need.
GUI_CHECK='
  apt-get install -y -qq --no-install-recommends xvfb xauth > /dev/null
  timeout 20 xvfb-run -a "$GUI" > /tmp/gui.log 2>&1
  rc=$?
  echo "GUI exit code $rc (124 = still running after 20 s)"
  if [ "$rc" -ne 124 ]; then tail -25 /tmp/gui.log; exit 1; fi'

APT_QUIET='export DEBIAN_FRONTEND=noninteractive; apt-get update -qq > /dev/null'

if [[ -n "${DEB}" ]]; then
  echo "::group::.deb: CLI on the sample scans"
  in_container "${APT_QUIET}; apt-get install -y -qq /pkgs/$(basename "${DEB}") > /dev/null && CLI=photocropper-cli; ${CLI_CHECK}"
  record ".deb: CLI finds ${EXPECTED} photos on the samples" $?
  echo "::endgroup::"

  echo "::group::.deb: GUI starts"
  in_container "${APT_QUIET}; apt-get install -y -qq /pkgs/$(basename "${DEB}") > /dev/null && GUI=photocropper; ${GUI_CHECK}"
  record ".deb: GUI starts" $?
  echo "::endgroup::"
else
  record ".deb: package found in ${PACKAGES}" 1
fi

if [[ -n "${APPIMAGE}" ]]; then
  # Extracted rather than mounted: containers have no FUSE, and the contents are the same. Both checks start the
  # app through AppRun, as the AppImage does (the CLI through a photocropper-cli link, as users set it up).
  EXTRACT="cd /tmp && cp /pkgs/$(basename "${APPIMAGE}") app && chmod +x app && ./app --appimage-extract > /dev/null"

  echo "::group::AppImage: CLI on the sample scans"
  in_container "${EXTRACT} && ln -s squashfs-root/AppRun /tmp/photocropper-cli && CLI=/tmp/photocropper-cli; ${CLI_CHECK}"
  record "AppImage: CLI finds ${EXPECTED} photos on the samples" $?
  echo "::endgroup::"

  # An AppImage relies on the libraries every desktop has (fontconfig, OpenGL, X11: the AppImage "excludelist"),
  # which a bare container lacks; install those, and nothing the app itself would need on top.
  DESKTOP_BASE='apt-get install -y -qq --no-install-recommends libfontconfig1 libgl1 libx11-6 libice6 libsm6 > /dev/null'
  echo "::group::AppImage: GUI starts"
  in_container "${APT_QUIET}; ${DESKTOP_BASE}; ${EXTRACT} && GUI=/tmp/squashfs-root/AppRun; ${GUI_CHECK}"
  record "AppImage: GUI starts" $?
  echo "::endgroup::"
else
  record "AppImage: package found in ${PACKAGES}" 1
fi

echo
echo "Linux package smoke tests:"
printf '  %s\n' "${results[@]}"
[[ "${failures}" -eq 0 ]]
