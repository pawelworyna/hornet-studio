#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-1.0.0}"
ARCH="${2:-linux-x64}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST_DIR="$ROOT_DIR/dist/$ARCH"
ARTIFACTS_DIR="$ROOT_DIR/artifacts/installer"

DEB_ARCH="amd64"
if [ "$ARCH" = "linux-arm64" ]; then
    DEB_ARCH="arm64"
fi

echo "=========================================================="
echo " HornetStudio - Linux DEB & Tarball Builder ($ARCH / $DEB_ARCH)"
echo " Version: $VERSION"
echo "=========================================================="

mkdir -p "$ARTIFACTS_DIR"
rm -rf "$DIST_DIR"

# 1. Publish self-contained .NET app for Linux
echo "Publishing self-contained .NET 11 desktop app ($ARCH)..."
dotnet publish "$ROOT_DIR/src/HornetStudio.Desktop/HornetStudio.Desktop.csproj" \
    -c Release \
    -r "$ARCH" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:Version="$VERSION" \
    -o "$DIST_DIR"

# 2. Assemble Debian package directory structure
PKG_NAME="hornetstudio_${VERSION}_${DEB_ARCH}"
PKG_DIR="$ROOT_DIR/dist/$PKG_NAME"
rm -rf "$PKG_DIR"

mkdir -p "$PKG_DIR/DEBIAN"
mkdir -p "$PKG_DIR/usr/lib/hornetstudio"
mkdir -p "$PKG_DIR/usr/bin"
mkdir -p "$PKG_DIR/usr/share/applications"
mkdir -p "$PKG_DIR/usr/share/icons/hicolor/512x512/apps"
mkdir -p "$PKG_DIR/usr/share/pixmaps"

# Copy published application files
cp -R "$DIST_DIR"/* "$PKG_DIR/usr/lib/hornetstudio/"
chmod +x "$PKG_DIR/usr/lib/hornetstudio/HornetStudio.Desktop"

# Create symlink launcher in /usr/bin
ln -s "/usr/lib/hornetstudio/HornetStudio.Desktop" "$PKG_DIR/usr/bin/hornetstudio"

# Copy Desktop entry and Icon
cp "$ROOT_DIR/installer/linux/hornetstudio.desktop" "$PKG_DIR/usr/share/applications/"
cp "$ROOT_DIR/src/HornetStudio.Desktop/Assets/hornet.png" "$PKG_DIR/usr/share/icons/hicolor/512x512/apps/hornetstudio.png"
cp "$ROOT_DIR/src/HornetStudio.Desktop/Assets/hornet.png" "$PKG_DIR/usr/share/pixmaps/hornetstudio.png"

# Create DEBIAN/control file
cat <<EOF > "$PKG_DIR/DEBIAN/control"
Package: hornetstudio
Version: $VERSION
Section: devel
Priority: optional
Architecture: $DEB_ARCH
Maintainer: HornetStudio Contributors <https://github.com/pawelworyna/hornet-studio>
Description: Fast, native, and modern database IDE
 HornetStudio is a compiled native database and BaaS workbench for PostgreSQL,
 MySQL, MariaDB, SQLite, MongoDB, Redis, DuckDB, ClickHouse, Oracle, SQL Server,
 CockroachDB, Redshift, PocketBase, Appwrite, Firebase, and AWS Amplify.
EOF

# 3. Build .deb package using dpkg-deb
if command -v dpkg-deb &>/dev/null; then
    echo "Building .deb package..."
    dpkg-deb --build --root-owner-group "$PKG_DIR" "$ARTIFACTS_DIR/$PKG_NAME.deb"
    echo "DEB package created at: $ARTIFACTS_DIR/$PKG_NAME.deb"
else
    echo "dpkg-deb not found; skipping .deb creation."
fi

# 4. Create Portable tar.gz
TAR_NAME="HornetStudio-linux-$ARCH-v$VERSION.tar.gz"
echo "Creating portable tarball..."
tar -czf "$ARTIFACTS_DIR/$TAR_NAME" -C "$DIST_DIR" .
echo "Tarball created at: $ARTIFACTS_DIR/$TAR_NAME"

echo "Linux packaging complete!"
