#!/usr/bin/env bash
set -euo pipefail

proof_source=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
proof_root=$(git -C "$proof_source" rev-parse --show-toplevel)
proof_output=${1:-"$proof_root/artifacts/crdt-public-native-proof"}
mkdir -p "$proof_output"
proof_output=$(cd -- "$proof_output" && pwd)
case "$proof_output/" in
    "$proof_source/bin/"*|"$proof_source/obj/"*)
        echo "Choose an artifact output outside this app's bin/ and obj/ cold-publish directories." >&2
        exit 1
        ;;
esac
mkdir -p "$proof_output/sdk-selection"
cat > "$proof_output/sdk-selection/global.json" <<'JSON'
{"sdk":{"version":"10.0.114","rollForward":"disable"}}
JSON
cd -- "$proof_output/sdk-selection"
proof_cli=${AKKA_NATIVE_DOTNET:-dotnet}
if [ "$("$proof_cli" --version)" != "10.0.114" ]; then
    echo "SDK 10.0.114 is required; point AKKA_NATIVE_DOTNET at an installed CLI containing it." >&2
    exit 1
fi
"$proof_cli" --info > "$proof_output/sdk-info.log"
proof_project="$proof_source/Akka.DistributedData.CRDT.AOT.App.csproj"
"$proof_cli" build "$proof_project" -c Release 2>&1 | tee "$proof_output/managed-build.log"
# These are this isolated consumer's generated outputs, never repository source.
rm -rf "$proof_source/bin" "$proof_source/obj"
"$proof_cli" publish "$proof_project" -c Release -r linux-x64 -o "$proof_output/publish" \
    2>&1 | tee "$proof_output/native-publish.log"
timeout 300 "$proof_output/publish/Akka.DistributedData.CRDT.AOT.App" \
    2>&1 | tee "$proof_output/native-run.log"
grep -Fxq '[crdt-public-native] OK cases=207 native-writes=207 segmented-reads=207 historical-reads=93 generic-writes-rejected=42 generic-reads-rejected=75 ownership=3 atomic=3 guarded-callbacks=6 unknown-manifests=6' \
    "$proof_output/native-run.log"
gzip -dc "$proof_source/baselines/reference-warning-keys.txt.gz" > "$proof_output/reference-warning-keys.txt"
"$proof_cli" run "$proof_root/scripts/CheckAotWarnings.cs" -- \
    --log "$proof_output/native-publish.log" --baseline "$proof_output/reference-warning-keys.txt" \
    --repo-root "$proof_root" --scope src/ \
    2>&1 | tee "$proof_output/native-warning-check.log"
grep -Eq '^  no source location: 0 warning' "$proof_output/native-warning-check.log"
