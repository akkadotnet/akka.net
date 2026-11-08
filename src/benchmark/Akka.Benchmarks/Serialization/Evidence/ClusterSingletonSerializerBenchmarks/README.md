# ClusterSingletonSerializerBenchmarks smoke receipts

SDK 10.0.114/runtime 10.0.14: build passed with zero warnings and errors; all 24 Dry setups completed and every child process exited zero. Slopwatch reported zero issues.

The six contracts compare byte-array writes, reads, and buffer writes using the V1 adapter versus the native V2 writer. Actual off/on actor-system bindings select ids 14/54. Setup validates both timed readers directly, opposite-binding table readers, full restored values, and wire sizes before timing. Singleton controls retain their original singleton identities.

The four genuine fieldless controls: HandOverToMe, HandOverInProgress, HandOverDone, TakeOverFromMe. Each is 0 bytes in protobuf and 1 byte in MessagePack.

Dry validates execution; Default/performance acceptance is pending. The retained single-iteration times are not performance results. Unprivileged process-priority and minimum-iteration notices are retained. These independent Dry runs ran concurrently with other builds and cannot establish throughput or allocation acceptance.

Run from an isolated checkout with SDK 10.0.114 available:

```bash
AKKA_BENCH_DOTNET=/path/to/dotnet \
  bash src/benchmark/Akka.Benchmarks/Serialization/Evidence/ClusterSingletonSerializerBenchmarks/reproduce.sh \
  /tmp/singleton-benchmark-final
```

Full output is compressed without filtering. `gzip -dc dry.log.gz` displays a receipt. `source-revision.txt.gz` records the parent revision and SHA-256 of the exact source, also retained as `source.cs.txt.gz`. `SHA256SUMS` checks every receipt and reproduction script; absolute machine paths in logs preserve provenance.
