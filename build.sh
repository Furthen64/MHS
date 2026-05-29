#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

REQUIRED_DOTNET_MAJOR=10

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Error: .NET SDK ${REQUIRED_DOTNET_MAJOR}.0+ is required, but 'dotnet' was not found."
  echo "Install it from: https://dotnet.microsoft.com/download/dotnet/${REQUIRED_DOTNET_MAJOR}.0"
  exit 1
fi

has_required_sdk=false
while IFS= read -r sdk_version; do
  [[ -z "$sdk_version" ]] && continue
  sdk_major="${sdk_version%%.*}"
  if [[ "$sdk_major" =~ ^[0-9]+$ ]] && (( sdk_major >= REQUIRED_DOTNET_MAJOR )); then
    has_required_sdk=true
    break
  fi
done < <(dotnet --list-sdks 2>/dev/null | awk '{print $1}')

if [[ "$has_required_sdk" != true ]]; then
  echo "Error: .NET SDK ${REQUIRED_DOTNET_MAJOR}.0+ is required to build this project."
  echo "Installed SDKs:"
  dotnet --list-sdks || true
  echo "Install .NET SDK ${REQUIRED_DOTNET_MAJOR}.0+ from: https://dotnet.microsoft.com/download/dotnet/${REQUIRED_DOTNET_MAJOR}.0"
  exit 1
fi

dotnet restore
dotnet build
