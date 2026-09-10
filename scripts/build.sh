#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
game_dir="${GAME_DIR:-$repo_root/UCH}"
bepinex_dir="${BEPINEX_DIR:-$game_dir/BepInEx}"
configuration="${CONFIGURATION:-Release}"

if [[ "${SKIP_RESTORE:-0}" != "1" ]]; then
  dotnet restore "$repo_root/UCHFixes.sln"
fi
dotnet build "$repo_root/src/UCHFixes/UCHFixes.csproj" \
  --configuration "$configuration" \
  --no-restore \
  -p:GameDir="$game_dir" \
  -p:BepInExDir="$bepinex_dir" \
  -p:UseSharedCompilation=false

dotnet build "$repo_root/tests/UCHFixes.Tests/UCHFixes.Tests.csproj" \
  --configuration "$configuration" \
  --no-restore \
  -p:UseSharedCompilation=false
DOTNET_ROLL_FORWARD=Major dotnet "$repo_root/tests/UCHFixes.Tests/bin/$configuration/net8.0/UCHFixes.Tests.dll"

package_root="$repo_root/artifacts/UCHFixes"
rm -rf "$package_root"
plugin_dir="$package_root/BepInEx/plugins/UCHFixes"
mkdir -p "$plugin_dir"
cp -f "$repo_root/src/UCHFixes/bin/$configuration/net48/UCHFixes.dll" "$plugin_dir/UCHFixes.dll"
cp -f "$repo_root/README.md" "$package_root/README.md"
cp -f "$repo_root/LICENSE" "$package_root/LICENSE"
mkdir -p "$package_root/docs"
cp -f "$repo_root/docs/reverse-engineering.md" "$package_root/docs/reverse-engineering.md"
cp -f "$repo_root/docs/linux-testing.md" "$package_root/docs/linux-testing.md"
(cd "$repo_root/artifacts" && rm -f "UCHFixes-0.1.0.zip" && zip -q -r "UCHFixes-0.1.0.zip" UCHFixes)

echo "Built plugin: $plugin_dir/UCHFixes.dll"
echo "Built package: $repo_root/artifacts/UCHFixes-0.1.0.zip"
