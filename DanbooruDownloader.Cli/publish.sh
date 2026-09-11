#!/bin/bash
# Builds a self-contained, single-file "danboo" binary for this Mac - no `dotnet run`, no
# .NET runtime install required to run it afterwards.
#
# Usage:
#   ./publish.sh                 # builds ./publish/danboo for this machine's architecture
#   sudo cp publish/danboo /usr/local/bin/danboo   # (optional) put it on your PATH
set -euo pipefail
cd "$(dirname "$0")"

RID=$(uname -m | grep -q arm64 && echo osx-arm64 || echo osx-x64)

dotnet publish -c Release -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:AssemblyName=danboo \
  -o ./publish

echo
echo "Built: $(pwd)/publish/danboo"
echo "Try it:      ./publish/danboo --help"
echo "Install it:  sudo cp publish/danboo /usr/local/bin/danboo"
