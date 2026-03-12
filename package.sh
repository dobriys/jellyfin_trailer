#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# package.sh  —  Build and package Jellyfin.Plugin.Trailer
#
# Produces: dist/Jellyfin.Plugin.Trailer_<version>.zip
#
# Usage:
#   chmod +x package.sh
#   ./package.sh                     # build + zip
#   ./package.sh --install           # build + zip + copy DLL to local Jellyfin
#   ./package.sh --update-manifest   # build + zip + patch dist/manifest.json
#                                    # (requires REPO_URL env var or prompts)
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

PROJECT="Jellyfin.Plugin.Trailer/Jellyfin.Plugin.Trailer.csproj"
DIST_DIR="dist"
INSTALL_DIR="${JELLYFIN_PLUGIN_DIR:-$HOME/.local/share/jellyfin/plugins/Jellyfin.Plugin.Trailer}"

# ── Read version from the csproj ─────────────────────────────────────────────
VERSION=$(grep '<Version>' "$PROJECT" | sed 's/.*<Version>\(.*\)<\/Version>.*/\1/' | tr -d '[:space:]')
OUTPUT_NAME="Jellyfin.Plugin.Trailer_${VERSION}"
ABS_ZIP="$(pwd)/${DIST_DIR}/${OUTPUT_NAME}.zip"

echo "━━━ Building v${VERSION} ━━━"
dotnet build "$PROJECT" -c Release --nologo -v minimal

echo ""
echo "━━━ Packaging → ${DIST_DIR}/${OUTPUT_NAME}.zip ━━━"

BUILD_OUTPUT="Jellyfin.Plugin.Trailer/bin/Release/net8.0"
mkdir -p "$DIST_DIR"

(
  cd "$BUILD_OUTPUT"
  zip -j "$ABS_ZIP" \
    "Jellyfin.Plugin.Trailer.dll" \
    "Jellyfin.Plugin.Trailer.xml" 2>/dev/null || \
  zip -j "$ABS_ZIP" \
    "Jellyfin.Plugin.Trailer.dll"
)

echo "Created: ${DIST_DIR}/${OUTPUT_NAME}.zip"

# ── MD5 checksum ─────────────────────────────────────────────────────────────
if command -v md5sum &>/dev/null; then
  CHECKSUM=$(md5sum "$ABS_ZIP" | awk '{print $1}')
elif command -v md5 &>/dev/null; then
  CHECKSUM=$(md5 -q "$ABS_ZIP")
else
  CHECKSUM="(md5 not available)"
fi
echo "MD5: ${CHECKSUM}"

# ── Optional: patch dist/manifest.json ───────────────────────────────────────
if [[ "${1:-}" == "--update-manifest" ]]; then
  echo ""
  echo "━━━ Updating dist/manifest.json ━━━"

  # Determine the download URL
  if [[ -z "${REPO_URL:-}" ]]; then
    echo "Enter the GitHub repo URL (e.g. https://github.com/username/jellyfin_trailer):"
    read -r REPO_URL
  fi
  REPO_URL="${REPO_URL%/}"   # strip trailing slash
  ZIP_URL="${REPO_URL}/releases/download/v${VERSION}/${OUTPUT_NAME}.zip"

  python3 update_manifest.py "$VERSION" "$CHECKSUM" "$ZIP_URL"
  echo ""
  echo "dist/manifest.json updated."
  echo "Commit and push it to make the new version available in Jellyfin."
fi

# ── Optional: install locally ─────────────────────────────────────────────────
if [[ "${1:-}" == "--install" ]]; then
  echo ""
  echo "━━━ Installing to ${INSTALL_DIR} ━━━"
  mkdir -p "$INSTALL_DIR"
  cp "${BUILD_OUTPUT}/Jellyfin.Plugin.Trailer.dll" "$INSTALL_DIR/"
  echo "Done. Restart Jellyfin to load the updated plugin."
fi
