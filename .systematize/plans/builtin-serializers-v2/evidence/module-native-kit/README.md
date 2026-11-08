# Focused module-native kit extraction

This branch starts at published Singleton `759852493` and merges the reviewed
Legacy exception/CI/SDK prerequisite `ba3aa8898` as `9673a2e976a7db3756de59898a00c5f429bc24e2`.
It copies only the existing app and module-canary pipeline section from stable
integration `bfb6b72f0`; no other production port implementations are imported.

At initial extraction, all C# and project files were byte-identical to the app in
both `bfb6b72f0` and positively tested composed source `053480698`.
`source-provenance.txt` retains those original hashes/comparisons. Final style
cleanup trims eight whitespace-only blank lines in Client/Containers/Metrics;
an ignore-space-at-eol comparison against both source commits reports no C#
differences. `style-only-comparison.txt` records that check; final working-file
and canonical git-blob hashes are retained separately. README
extraction/dependency documentation is new;
the warning inventory's historical header count is corrected from 19 to 18,
with all 18 existing keys unchanged. The existing pipeline section is copied
exactly; its snapshot/hash are retained separately.

SDK 10.0.114 managed compilation passed with zero warnings/errors. Slopwatch
passed with zero findings. All source files were inspected: the app keeps its
ordinary identity, uses public APIs, and has no friend identity, private product
type references, reflective bootstrap, or blanket assembly roots. Private
protocol values are obtained through public old-reader dispatch over frozen
records. Consumer-owned payload codecs use the existing public setup APIs.

Compile command, from the isolated checkout:

```bash
PATH=/tmp/akka-serialization-v2/toolchain-install:$PATH \
DOTNET_ROOT=/tmp/akka-serialization-v2/toolchain-install \
/tmp/akka-serialization-v2/toolchain-install/dotnet build \
  src/aot/Akka.Serialization.V2.AOT.App -c Release
```

The isolated branch was **not run as a complete native runtime proof**. It
intentionally lacks the unmerged reader registrations and implementations in
Metrics #8917, PubSub #8919, Client #8922, Containers #8923, Snapshot #8925,
Persistence #8927, Streams #8929 and Misc #8934. Those are runtime dependencies,
not optional tests; missing readers or wrong bindings fail the unchanged gates.
`runtime-dependencies.json` records their public PR identities/heads at extraction
time; it does not identify the earlier composed proof's source heads.

The runtime-positive evidence remains the exact app's composed source at
`053480698`, retained by `652b9c83a` and copied unchanged under
`../integration-053480698`. Those logs establish all 249 fixed cases on that
composition, with 18 known scoped warning identities, zero new scoped keys,
zero unlocated diagnostics and one Core warning outside the module scope. Core
has its separate rooted canary. The original stale nested actor-ref binding
failure is retained alongside the successful final run; the correction requires
Misc id 56 under global on even when only the outer container is pinned.

The completion marker and every per-module count/native-write gate are unchanged.
Snapshot/Persistence retain ids 8/7 and identical bytes; their selection does not
depend on the global V2 toggle. Generic FSM envelopes, generic Delivery, bare
stream refs and native Remote/Cluster startup remain explicit boundaries.
Hosted CI for this extraction is pending, and cannot be claimed green before
its production dependencies land or are composed. No Default measurement or
upstream merge was performed.
