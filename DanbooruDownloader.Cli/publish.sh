#!/bin/bash
# Builds a self-contained, single-file "danboo" binary for this machine (macOS, Linux, or
# Windows under Git Bash) - no `dotnet run`, no .NET runtime install required to run it.
#
# Usage:
#   ./publish.sh                 # builds ./publish/danboo (danboo.exe on Windows)
#   ./publish.sh linux-x64       # or name another .NET runtime identifier
set -euo pipefail
cd "$(dirname "$0")"

ARCH=$(uname -m | grep -qE 'arm64|aarch64' && echo arm64 || echo x64)
case "$(uname -s)" in
  Darwin) OS=osx ;;
  Linux) OS=linux ;;
  MINGW*|MSYS*|CYGWIN*) OS=win ;;
  *) echo "Unknown OS; pass a runtime identifier, e.g. ./publish.sh linux-x64" >&2; exit 1 ;;
esac
RID=${1:-$OS-$ARCH}

dotnet publish -c Release -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:AssemblyName=danboo \
  -p:DebugType=none \
  -o ./publish

echo
echo "Built for $RID: $(pwd)/publish"
echo "Try it:      ./publish/danboo --help"
if [[ "$RID" != win-* ]]; then
  echo "Install it:  sudo cp publish/danboo /usr/local/bin/danboo"
fi
