# Final composed module native canary

Tested source: `053480698`, SDK 10.0.114 / native compiler and runtime 10.0.14, linux-x64.
All 249 existing fixed cases pass with the new System/Misc/Daemon registrations and finite
historical exception reader installed. The app's project graph does not include Sharding or
DistributedData; their managed composed recovery proof remains separately scoped.

The initial run correctly failed a stale test assumption: actor-reference payload writers were
hard-coded to legacy id 16. With Misc V2 installed they must select 56 under global on, even
when only the outer container is pinned to legacy. The corrected canary requires exact id 16
off and exact id 56 on/container-only-pin, preserves all 30 container cases and six actor-ref
payload controls, and completes every other module's required assertions. The failed run and
its full publish output are retained alongside the final passing run.

Warning checker: 18 known scoped keys, zero new scoped keys, one source-located Core warning
outside the module scope, zero warnings without a source location. The exception-reader
reflection key is no longer emitted and is removed from this preview's baseline. No new
warning is approved. Source, commands and exact CI completion marker remain in the app README.

This proves ordinary local-provider codec paths with dynamic loading disabled, not native
Remote/Cluster startup, remote deployment, generic Delivery/FSM or bare stream refs. Subsequent
System/Misc corpus extensions have their own source/receipts and are not counted into 249.
