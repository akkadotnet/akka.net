# Built-in Serialization V2 native canary

Incremental proof for [#8718](https://github.com/akkadotnet/akka.net/issues/8718), run with
`Akka.DynamicTypeLoading=false` as an actual native executable. All actor systems use a local
provider; transport, cluster activation and generic reconstruction are separate claims.

This focused follow-up extracts the existing integration canary without merging the other
production ports. Its isolated branch builds with public APIs, but its full native runtime
gate requires the unmerged reader ports: [Metrics #8917](https://github.com/akkadotnet/akka.net/pull/8917),
[PubSub #8919](https://github.com/akkadotnet/akka.net/pull/8919),
[Client #8922](https://github.com/akkadotnet/akka.net/pull/8922),
[Containers #8923](https://github.com/akkadotnet/akka.net/pull/8923),
[Snapshot #8925](https://github.com/akkadotnet/akka.net/pull/8925),
[Persistence #8927](https://github.com/akkadotnet/akka.net/pull/8927),
[Streams #8929](https://github.com/akkadotnet/akka.net/pull/8929), and
[Misc #8934](https://github.com/akkadotnet/akka.net/pull/8934) for nested actor-reference
bindings. Missing registrations fail; no module or assertion is skipped. The branch includes
the existing Singleton canary and the reviewed Legacy exception/SDK prerequisite #8935/#8914.
The positive 249-case native proof passed on composed source `053480698`, originally recorded in `652b9c83a`. This is not an isolated-branch runtime pass. The isolated extraction compiles with SDK 10.0.114 with zero warnings/errors; it has no isolated complete native run.

| Protocol | Legacy / V2 ids | Executed fixture cases |
|---|---|---|
| Singleton | 14 / 54 | 12; identity, native buffer writes, nine expected rejections |
| Metrics | 10 / 50 | 24; eight required native writes, every numeric kind/value, timestamps, EWMA, router selectors and all five pins |
| PubSub | 9 / 49 | 51; seventeen required native writes, fifteen frozen legacy shapes including nulls, bytes and live actor-reference payloads |
| Cluster Client | 15 / 55 | 48; sixteen frozen reader pairs including nulls, nine custom payload envelopes and nine rejections |
| Actor-selection containers | 6 / 46 | 30; ten reader pairs including nulls, six actor-reference payloads, wildcard flag and seven rejections |
| Snapshot envelope | 8, same bytes | 15; five frozen legacy shapes across default/stripped/versioned old serializer names, required native writes, segmented reads, six user payloads and nine rejections |
| Persistence messages | 7, same bytes | 33; eleven fixed legacy shapes across three serializer-name spellings, required native writes, six manifest-alias reads, six user payloads and thirty-three rejections |
| Stream-ref protocol | 30 / 70 | 36; all five forms, fourteen required writes, six user payloads, twelve bare-ref rejections |

The two Persistence implementations retain their original ids, manifests and protobuf bytes;
the global V2 switch does not select them. The canary requires the native buffer writer for
both default registrations and for copied old public serializer names. Id 7 covers persistent
records, atomic writes, at-least-once delivery snapshots and fixed FSM state changes.
Generic `PersistentFSMSnapshot<T>` writes and reads must reject before caller output when
dynamic type loading is disabled; this is not native PersistentFSM support.

These consumers use public serialization APIs without a test assembly identity or blanket
assembly roots. PubSub's private domain types are obtained from frozen legacy records; their
fields are checked by re-encoding with the unchanged legacy writer. The client explicitly sets
its existing legacy-serialization setting off; bare stream refs and whole client delivery are
not claimed. Unknown paths must fail before the completion marker is printed.

This app is separate from the plain-core canary so adding a module does not change the
core-only boot and warning surface. It reuses `Shared/LogWatchdogFilter.cs`; runtime warnings
or errors fail the run. Later ports extend this corpus with actual round trips, including
generic payload reconstruction where applicable. Registering an id alone is not native proof.

## Run

Use the serviced SDK from `global.json` after the SDK update in #8914 (the local proof used
SDK 10.0.114 / runtime 10.0.14). Keep audit and warning checks enabled. Passing
`-p:PublishAot=true` globally would also change the library builds; the app enables it locally
when a runtime identifier is supplied.

```bash
set -o pipefail
rm -rf src/aot/Akka.Serialization.V2.AOT.App/bin src/aot/Akka.Serialization.V2.AOT.App/obj
dotnet publish src/aot/Akka.Serialization.V2.AOT.App -r linux-x64 -c Release \
  -o /tmp/v2-aot-publish 2>&1 | tee /tmp/v2-aot-publish.log
timeout 300 /tmp/v2-aot-publish/Akka.Serialization.V2.AOT.App
```

The run must exit 0 and print `[canary-v2] OK cases=12 metrics=24 pubsub=51 client=48 containers=30 snapshot=15 persistence=33 streams=36 native-writes=4 rejected=9`.
The original 165 fixture cases remain required; Persistence adds 48 and Streams adds 36 for a total of 249.
Each module also checks its own exact native-write and rejection counts before returning.
The runtime guard deliberately rejects managed execution; ordinary solution builds still
compile this app without native publishing.

## Warning and coverage boundaries

The cold-publish warning baseline covers Remote, Cluster, Persistence, Streams, the V2 runtime, Cluster.Tools, Cluster.Metrics and this app. Existing
legacy reflection warnings remain visible and are tracked individually; the Singleton port
adds none. The two Persistence entries are copied exactly from the existing Persistence canary
baseline after this cold publish observed them; they belong to the unchanged generic FSM
legacy reader/writer, not the new fixed-shape implementation. Core's separate rooted canary
owns the Akka.dll baseline. Dependencies without source
locations are counted separately by the checker and are not approved by this baseline.

```bash
dotnet run scripts/CheckAotWarnings.cs -- \
  --log /tmp/v2-aot-publish.log \
  --baseline src/aot/Akka.Serialization.V2.AOT.App/aot-warnings.baseline.txt \
  --scope "src/core/Akka.Remote/,src/core/Akka.Cluster/,src/core/Akka.Persistence/,src/core/Akka.Streams/,src/core/Akka.Serialization.V2/,src/contrib/cluster/Akka.Cluster.Tools/,src/contrib/cluster/Akka.Cluster.Metrics/,src/aot/Akka.Serialization.V2.AOT.App/" \
  --repo-root .
```

CI retains the full publish/run logs and checks both the exit status and completion marker.
Passing this corpus establishes these native codec paths, not Remote/Cluster startup,
network interoperability, singleton ownership hand-over, persistence journal/snapshot-store
activation or recovery, or arbitrary generic payload support.
