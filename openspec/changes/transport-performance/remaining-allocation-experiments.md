# Remaining transport allocation experiments

Active user-authorized goal, 2026-10-16. Baseline is #8813 head 0879aceea. Literal caching/compression remains excluded. Primary dev checkout stays untouched; all experiments use isolated worktrees.

| Sequence | Candidate | Control and decision evidence |
|---|---|---|
| 1 | TCP read closure and double async wrapper | Existing TcpOperationsBenchmarks, 10/100-byte payloads, one client; byte validation and exact configured echo count. Baseline/candidate all TCP tests and API approvals. Keep copy and Tell unchanged. |
| 2 | AsyncInput boxes in same-actor short-circuit queue | Existing RestartFlow and Queue ingress controls, external/reference paths, basic and one-element restart materialization. Preserve a single FIFO, quotas, feedback, tracing and actor boundaries. |
| 3 | StageActor sender/message tuple boxes | Actual StageActor reference-ingress and traced controls in existing SourceActorRefBenchmarks. Public adapters preserved; compare independently on top of typed-queue commit. |
| 4 | Outbound owner/segment metadata | Existing envelope encoding controls for one and two frames using actual multi-lane batch append. Poisoned pools, transfers, growth, failures and cancellation; retain public Detach spent behavior. Writer and TCP wrapper may remain. |
| 5 | Receive copy and explicit ownership | Actual Pipe + production frame parser + asynchronous consuming actor, copy/borrow controls for whole/fragmented/delayed consumers. Stable ordinary TCP Received remains; partial tails and lane retention must be owned. Arbitrary serializers can retain input, so transparent zero-copy is not authorized by this prototype. |

All viable experiments need generated BenchmarkDotNet output, source/runtimeconfig/binary hashes and retained raw evidence. Reuse existing benchmark classes without adding a global parameter matrix. Benchmark and RemotePingPong workloads run sequentially under one measurement owner, with correctness builds/tests paused. Match fixed GC settings, retain every observation, and disclose shared-host load and sampled-profile limits. Successful production changes become small labeled stacked PRs with generated tables inline. Reject experiments lacking a demonstrated benefit or violating ownership/compatibility; report measured results and required follow-up design explicitly. No merge authorization.
