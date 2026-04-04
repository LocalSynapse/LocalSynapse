#!/bin/bash
# app-icon.ico에서 app-icon.icns를 생성한다.
# macOS의 sips + iconutil 사용.

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
SOURCE_ICO="$REPO_ROOT/assets/app-icon.ico"
OUTPUT_ICNS="$REPO_ROOT/assets/app-icon.icns"
ICONSET_DIR="$REPO_ROOT/assets/app-icon.iconset"

# 1. ICO → PNG (가장 큰 해상도 추출)
TMP_PNG="/tmp/localsynapse-icon-source.png"
sips -s format png "$SOURCE_ICO" --out "$TMP_PNG" >/dev/null 2>&1

# 2. iconset 디렉토리 생성
rm -rf "$ICONSET_DIR"
mkdir -p "$ICONSET_DIR"

# 3. 각 해상도 생성
sips -z 16 16       "$TMP_PNG" --out "$ICONSET_DIR/icon_16x16.png"      >/dev/null 2>&1
sips -z 32 32       "$TMP_PNG" --out "$ICONSET_DIR/icon_16x16@2x.png"   >/dev/null 2>&1
sips -z 32 32       "$TMP_PNG" --out "$ICONSET_DIR/icon_32x32.png"      >/dev/null 2>&1
sips -z 64 64       "$TMP_PNG" --out "$ICONSET_DIR/icon_32x32@2x.png"   >/dev/null 2>&1
sips -z 128 128     "$TMP_PNG" --out "$ICONSET_DIR/icon_128x128.png"    >/dev/null 2>&1
sips -z 256 256     "$TMP_PNG" --out "$ICONSET_DIR/icon_128x128@2x.png" >/dev/null 2>&1
sips -z 256 256     "$TMP_PNG" --out "$ICONSET_DIR/icon_256x256.png"    >/dev/null 2>&1
sips -z 512 512     "$TMP_PNG" --out "$ICONSET_DIR/icon_256x256@2x.png" >/dev/null 2>&1
sips -z 512 512     "$TMP_PNG" --out "$ICONSET_DIR/icon_512x512.png"    >/dev/null 2>&1
sips -z 1024 1024   "$TMP_PNG" --out "$ICONSET_DIR/icon_512x512@2x.png" >/dev/null 2>&1

# 4. iconset → icns
iconutil -c icns "$ICONSET_DIR" -o "$OUTPUT_ICNS"

# 5. 정리
rm -rf "$ICONSET_DIR"
rm -f "$TMP_PNG"

echo "Created: $OUTPUT_ICNS ($(du -h "$OUTPUT_ICNS" | cut -f1))"
