#!/usr/bin/env bash
set -euo pipefail

# Publish the shared Avalonia/PortAudio app for the current host. Set RID to
# cross-publish (for example RID=linux-x64 on macOS); only host builds can be
# launched and hardware-tested here.
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet="${DOTNET:-dotnet}"
case "$(uname -s)-$(uname -m)" in
  Darwin-arm64) host_rid=osx-arm64 ;;
  Darwin-x86_64) host_rid=osx-x64 ;;
  Linux-x86_64) host_rid=linux-x64 ;;
  Linux-aarch64) host_rid=linux-arm64 ;;
  *) echo "Unsupported host architecture. Set RID to a supported runtime identifier." >&2; exit 1 ;;
esac
rid="${RID:-$host_rid}"
case "$rid" in
  osx-arm64|osx-x64|linux-x64|linux-arm64) ;;
  *) echo "Unsupported RID: $rid" >&2; exit 1 ;;
esac

output="$repo/dist/$rid"
mkdir -p "$output"
"$dotnet" publish "$repo/src/RyanMusicStudio.Desktop/RyanMusicStudio.Desktop.csproj" \
  -c Release -r "$rid" --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -p:UsedAvaloniaProducts= -o "$output"

if [[ "$rid" == osx-* ]]; then
  bundle="$repo/dist/RMS-$rid.app"
  rm -rf "$bundle"
  mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
  cp "$output/RMS" "$output"/*.dylib "$bundle/Contents/MacOS/"
  cat > "$bundle/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>RMS</string>
<key>CFBundleDisplayName</key><string>RMS — Ryan Music Studio</string>
<key>CFBundleIdentifier</key><string>com.ryanhoopes.rms</string>
<key>CFBundleExecutable</key><string>RMS</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>1.2.0</string>
<key>CFBundleVersion</key><string>1.2.0</string>
<key>NSMicrophoneUsageDescription</key><string>RMS needs the microphone to record your vocal and guitar takes.</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
  if [[ "$(uname -s)" == Darwin ]]; then
    codesign --force --deep --sign - "$bundle"
    ditto -c -k --keepParent "$bundle" "$repo/dist/RMS-$rid.zip"
    echo "Built $repo/dist/RMS-$rid.zip"
  else
    echo "Built $bundle (sign and zip on macOS before distributing)"
  fi
else
  tar -C "$repo/dist" -czf "$repo/dist/RMS-$rid.tar.gz" "$rid"
  echo "Built $repo/dist/RMS-$rid.tar.gz"
fi
