#!/usr/bin/env bash
set -euo pipefail
# Run from an isolated repository checkout, with SDK 10.0.114 available.
: "${AKKA_BENCH_DOTNET:?Set AKKA_BENCH_DOTNET to the full SDK 10.0.114 dotnet path}"
export DOTNET_ROOT="$(dirname "$AKKA_BENCH_DOTNET")"
export PATH="$DOTNET_ROOT:$PATH"
output="${1:-/tmp/pubsub-benchmark-final}"
mkdir -p "$output"
"$AKKA_BENCH_DOTNET" --info > "$output/sdk-info.log"
git rev-parse HEAD > "$output/source-revision.txt"
sha256sum src/benchmark/Akka.Benchmarks/Serialization/DistributedPubSubSerializerBenchmarks.cs >> "$output/source-revision.txt"
git diff -- src/benchmark/Akka.Benchmarks/Akka.Benchmarks.csproj > "$output/project-reference.diff"
"$AKKA_BENCH_DOTNET" build src/benchmark/Akka.Benchmarks -c Release 2>&1 | tee "$output/build.log"
"$AKKA_BENCH_DOTNET" run --project src/benchmark/Akka.Benchmarks -c Release --no-build -- \
  --filter '*DistributedPubSubSerializerBenchmarks*' --job Dry --artifacts "$output/artifacts" 2>&1 | tee "$output/dry.log"
grep -Fq 'executed benchmarks: 24' "$output/dry.log"
test "$(grep -Fc 'has exited with code 0' "$output/dry.log")" -eq 24
slopwatch analyze --files src/benchmark/Akka.Benchmarks/Serialization/DistributedPubSubSerializerBenchmarks.cs --no-baseline --fail-on warning 2>&1 | tee "$output/slopwatch.log"
