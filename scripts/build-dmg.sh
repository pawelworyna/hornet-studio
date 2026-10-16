#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-1.0.0}"
ARCH="${2:-osx-arm64}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST_DIR="$ROOT_DIR/dist/$ARCH"
ARTIFACTS_DIR="$ROOT_DIR/artifacts/installer"
APP_DIR="$ROOT_DIR/dist/HornetStudio.app"

echo "=========================================================="
echo " HornetStudio - macOS DMG & App Builder ($ARCH)"
echo " Version: $VERSION"
echo "=========================================================="

mkdir -p "$ARTIFACTS_DIR"
rm -rf "$DIST_DIR" "$APP_DIR"

# 1. Publish self-contained .NET app for macOS
echo "Publishing self-contained .NET 11 desktop app ($ARCH)..."
dotnet publish "$ROOT_DIR/src/HornetStudio.Desktop/HornetStudio.Desktop.csproj" \
    -c Release \
    -r "$ARCH" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:Version="$VERSION" \
    -o "$DIST_DIR"

# 2. Assemble HornetStudio.app structure
echo "Assembling macOS .app bundle..."
mkdir -p "$APP_DIR/Contents/MacOS"
mkdir -p "$APP_DIR/Contents/Resources"

cp -R "$DIST_DIR"/* "$APP_DIR/Contents/MacOS/"
chmod +x "$APP_DIR/Contents/MacOS/HornetStudio.Desktop"

sed "s/1.0.0/$VERSION/g" "$ROOT_DIR/installer/macos/Info.plist" > "$APP_DIR/Contents/Info.plist"

PNG_ICON="$ROOT_DIR/src/HornetStudio.Desktop/Assets/hornet.png"
if command -v iconutil &>/dev/null && command -v sips &>/dev/null && [ -f "$PNG_ICON" ]; then
    echo "Generating ICNS icon bundle..."
    ICONSET_DIR="$ROOT_DIR/dist/AppIcon.iconset"
    mkdir -p "$ICONSET_DIR"
    sips -z 16 16     "$PNG_ICON" --out "$ICONSET_DIR/icon_16x16.png"
    sips -z 32 32     "$PNG_ICON" --out "$ICONSET_DIR/icon_16x16@2x.png"
    sips -z 32 32     "$PNG_ICON" --out "$ICONSET_DIR/icon_32x32.png"
    sips -z 64 64     "$PNG_ICON" --out "$ICONSET_DIR/icon_32x32@2x.png"
    sips -z 128 128   "$PNG_ICON" --out "$ICONSET_DIR/icon_128x128.png"
    sips -z 256 256   "$PNG_ICON" --out "$ICONSET_DIR/icon_128x128@2x.png"
    sips -z 256 256   "$PNG_ICON" --out "$ICONSET_DIR/icon_256x256.png"
    sips -z 512 512   "$PNG_ICON" --out "$ICONSET_DIR/icon_256x256@2x.png"
    sips -z 512 512   "$PNG_ICON" --out "$ICONSET_DIR/icon_512x512.png"
    iconutil -c icns "$ICONSET_DIR" -o "$APP_DIR/Contents/Resources/hornet.icns"
    rm -rf "$ICONSET_DIR"
fi

# 3. Create DMG (Disk Image)
DMG_NAME="HornetStudio-v$VERSION-$ARCH.dmg"
DMG_PATH="$ARTIFACTS_DIR/$DMG_NAME"
rm -f "$DMG_PATH"

if command -v hdiutil &>/dev/null; then
    echo "Creating DMG using hdiutil..."
    DMG_STAGE="$ROOT_DIR/dist/dmg-stage"
    rm -rf "$DMG_STAGE"
    mkdir -p "$DMG_STAGE"
    cp -R "$APP_DIR" "$DMG_STAGE/"
    ln -s /Applications "$DMG_STAGE/Applications"
    
    hdiutil create -volname "HornetStudio" -srcfolder "$DMG_STAGE" -ov -format UDZO "$DMG_PATH"
    rm -rf "$DMG_STAGE"
    echo "DMG created at: $DMG_PATH"
fi

# 4. Create Portable ZIP for macOS
ZIP_PATH="$ARTIFACTS_DIR/HornetStudio-Portable-$ARCH-v$VERSION.zip"
rm -f "$ZIP_PATH"
(cd "$ROOT_DIR/dist" && zip -r -q "$ZIP_PATH" "HornetStudio.app")
echo "Portable ZIP created at: $ZIP_PATH"

echo "macOS packaging complete!"
