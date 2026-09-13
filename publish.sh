#!/usr/bin/env bash
# Builds standalone, self-contained binaries — no .NET runtime needs to be
# installed on the machine that runs them. Output:
#   publish/linux-x64/GameArtMatch     (run directly, or chmod +x if needed)
#   publish/win-x64/GameArtMatch.exe
set -euo pipefail
cd "$(dirname "$0")"

RIDS=(linux-x64 win-x64)

for rid in "${RIDS[@]}"; do
  echo "=== Publishing $rid ==="
  dotnet publish -c Release -r "$rid" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "publish/$rid"
done

echo
echo "Done:"
find publish -maxdepth 2 -type f \( -name "GameArtMatch" -o -name "GameArtMatch.exe" \) -exec ls -lh {} \;
