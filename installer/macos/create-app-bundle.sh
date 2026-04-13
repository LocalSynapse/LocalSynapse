#!/bin/bash
# dotnet publish 출력물로 .app 번들과 .dmg를 생성한다.
# Usage: create-app-bundle.sh <version> <rid> <publish-dir>
# Example: create-app-bundle.sh 2.3.0 osx-arm64 ./publish/osx-arm64

set -e

VERSION="$1"
RID="$2"
PUBLISH_DIR="$3"

if [ -z "$VERSION" ] || [ -z "$RID" ] || [ -z "$PUBLISH_DIR" ]; then
    echo "Usage: $0 <version> <rid> <publish-dir>"
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Architecture label for filename
if [ "$RID" = "osx-arm64" ]; then
    ARCH_LABEL="AppleSilicon"
elif [ "$RID" = "osx-x64" ]; then
    ARCH_LABEL="Intel"
else
    ARCH_LABEL="$RID"
fi

APP_NAME="LocalSynapse"
APP_BUNDLE="$REPO_ROOT/publish/macos/$APP_NAME.app"
DMG_NAME="LocalSynapse-v${VERSION}-macOS-${ARCH_LABEL}.dmg"
DMG_OUTPUT="$REPO_ROOT/publish/macos/$DMG_NAME"

echo "=== Creating $APP_NAME.app bundle (${RID}) ==="

# 1. .app 번들 구조 생성
rm -rf "$APP_BUNDLE"
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

# 2. Info.plist 복사 + 버전 치환
sed "s|__VERSION__|$VERSION|g" "$SCRIPT_DIR/Info.plist.template" > "$APP_BUNDLE/Contents/Info.plist"

# 3. 퍼블리시 파일 복사
cp -R "$PUBLISH_DIR"/* "$APP_BUNDLE/Contents/MacOS/"

# 4. 실행 권한 부여
chmod +x "$APP_BUNDLE/Contents/MacOS/LocalSynapse"

# 5. 아이콘 복사
ICNS_FILE="$REPO_ROOT/assets/app-icon.icns"
if [ -f "$ICNS_FILE" ]; then
    cp "$ICNS_FILE" "$APP_BUNDLE/Contents/Resources/app-icon.icns"
    echo "Icon copied: app-icon.icns"
else
    echo "Warning: app-icon.icns not found, skipping icon"
fi

echo "App bundle created: $APP_BUNDLE"

# 6. Ad-hoc 코드 서명
codesign --force --deep -s - "$APP_BUNDLE"
echo "Ad-hoc signed: $APP_BUNDLE"

# 7. DMG 생성
echo "=== Creating DMG: $DMG_NAME ==="
rm -f "$DMG_OUTPUT"
mkdir -p "$(dirname "$DMG_OUTPUT")"

DMG_BG="$REPO_ROOT/assets/dmg/dmg-background.png"
DMG_README="$REPO_ROOT/assets/dmg/README.txt"

# create-dmg가 설치되어 있으면 사용 (배경 + 아이콘 배치)
if command -v create-dmg &>/dev/null; then
    echo "Using create-dmg..."

    CREATE_DMG_ARGS=(
        --volname "LocalSynapse"
        --window-pos 200 120
        --window-size 640 750
        --icon-size 80
        --icon "LocalSynapse.app" 180 100
        --app-drop-link 460 100
        --no-internet-enable
    )

    # 배경 이미지 (있으면 사용, 1280x960 Retina 2x)
    if [ -f "$DMG_BG" ]; then
        CREATE_DMG_ARGS+=(--background "$DMG_BG")
    fi

    # create-dmg는 0이 아닌 exit code를 반환할 수 있음 (경고)
    create-dmg "${CREATE_DMG_ARGS[@]}" "$DMG_OUTPUT" "$APP_BUNDLE" || true

    if [ ! -f "$DMG_OUTPUT" ]; then
        echo "create-dmg failed, falling back to hdiutil..."
    fi
fi

# create-dmg 실패 시 또는 미설치 시 hdiutil 폴백
if [ ! -f "$DMG_OUTPUT" ]; then
    echo "Using hdiutil..."
    DMG_STAGING="$REPO_ROOT/publish/macos/dmg-staging"
    rm -rf "$DMG_STAGING"
    mkdir -p "$DMG_STAGING"

    cp -R "$APP_BUNDLE" "$DMG_STAGING/"
    ln -s /Applications "$DMG_STAGING/Applications"

    hdiutil create -volname "LocalSynapse" \
        -srcfolder "$DMG_STAGING" \
        -ov -format UDZO \
        "$DMG_OUTPUT"

    rm -rf "$DMG_STAGING"
fi

# 정리
rm -rf "$APP_BUNDLE"

DMG_SIZE=$(du -h "$DMG_OUTPUT" | cut -f1)
echo "DMG created: $DMG_OUTPUT ($DMG_SIZE)"

# SHA256
DMG_SHA256=$(shasum -a 256 "$DMG_OUTPUT" | awk '{print $1}')
echo "SHA256: $DMG_SHA256"
echo "$DMG_SHA256  $DMG_NAME" >> "$REPO_ROOT/publish/macos/SHA256SUMS-macOS.txt"
