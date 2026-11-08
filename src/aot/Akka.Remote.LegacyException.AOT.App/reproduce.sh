#!/usr/bin/env bash
set -euo pipefail

proof_source=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
proof_root=$(git -C "$proof_source" rev-parse --show-toplevel)
proof_output=${1:-"$proof_source/bin/native-proof"}
mkdir -p "$proof_output"
proof_output=$(cd -- "$proof_output" && pwd)
mkdir -p "$proof_output/sdk-selection"
# The base branch's older global.json must not silently select SDK 10.0.101.
# This app-local work directory selects the serviced SDK without changing the repo pin.
cat > "$proof_output/sdk-selection/global.json" <<'JSON'
{"sdk":{"version":"10.0.114","rollForward":"disable"}}
JSON
cd -- "$proof_output/sdk-selection"
proof_cli=${AKKA_NATIVE_DOTNET:-dotnet}
if [ "$("$proof_cli" --version)" != "10.0.114" ]; then
    echo "SDK 10.0.114 is required. Set AKKA_NATIVE_DOTNET to an installed dotnet CLI containing it." >&2
    exit 1
fi
"$proof_cli" --info > "$proof_output/sdk-info.log"
proof_project="$proof_source/Akka.Remote.LegacyException.AOT.App.csproj"
"$proof_cli" clean "$proof_project" -c Release -r linux-x64 --nologo > "$proof_output/clean.log" 2>&1
"$proof_cli" test "$proof_root/src/core/Akka.Remote.Tests" -c Release --framework net10.0 --nologo \
    --filter 'FullyQualifiedName~ExceptionSupportSpec|FullyQualifiedName~ExceptionMessagePackFormatterSpec|FullyQualifiedName~LegacyExceptionNativeReadSpec|FullyQualifiedName~SystemMessageSerializationSpec|FullyQualifiedName~MiscMessageSerializerSpec' \
    2>&1 | tee "$proof_output/focused-tests.log"
"$proof_cli" test "$proof_root/src/core/Akka.API.Tests" -c Release --framework net10.0 --nologo \
    2>&1 | tee "$proof_output/api-tests.log"
"$proof_cli" publish "$proof_project" -c Release -r linux-x64 --nologo -o "$proof_output/publish" \
    2>&1 | tee "$proof_output/native-publish.log"
timeout 300 "$proof_output/publish/Akka.Remote.LegacyException.AOT.App" \
    2>&1 | tee "$proof_output/native-run.log"
grep -qF '[legacy-exceptions-native] OK reads=114 rejected=15 overrides=3 empty-sentinels=3' "$proof_output/native-run.log"
"$proof_cli" run "$proof_root/scripts/CheckAotWarnings.cs" -- \
    --log "$proof_output/native-publish.log" \
    --baseline "$proof_source/baselines/previous-remote-warning-keys.txt" \
    --repo-root "$proof_root" \
    --scope 'src/core/Akka.Remote/,src/core/Akka.Serialization.V2/,src/aot/Akka.Remote.LegacyException.AOT.App/' \
    2>&1 | tee "$proof_output/native-warning-check.log"
