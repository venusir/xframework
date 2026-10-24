#!/usr/bin/env bash
# 用法：LUBAN_DLL=/path/to/Luban.dll ./gen.sh
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dotnet "${LUBAN_DLL:?set LUBAN_DLL}" -t client -c cs-simple-json -d json \
    --conf "$root/luban.conf" \
    -x "outputCodeDir=$root/../Generated/Code" \
    -x "outputDataDir=$root/../Data"
