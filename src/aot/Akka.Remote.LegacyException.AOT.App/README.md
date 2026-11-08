# Historical exception native consumer

This ordinary consumer reads original protobuf exception records through public serializer ids
`16` and `22`. It requires an actual native executable and `Akka.DynamicTypeLoading=false`.
There is no friend assembly identity, private product DTO access or blanket assembly root.

`OriginalFixtures.cs` retains 40 original records captured before the fix from
`ExceptionSupport` git blob `ab1ac13716307407ad93b85c807c55ee07388e28`. The same records are
retained as annotated hex dumps in
[`Akka.Remote.Tests/Serialization/Snapshots`](../../core/Akka.Remote.Tests/Serialization/Snapshots),
with their capture provenance in
[`LegacyExceptionNativeReadSpec`](../../core/Akka.Remote.Tests/Serialization/LegacyExceptionNativeReadSpec.cs).
`NegativeFixtures.cs` labels three synthetic controls derived from those records; they are
not historical valid writer output.

The executable reads 38 supported records under off/on/pin settings, for 114 reads. It checks
types, the historical message behavior, source/stack values, inner exceptions, actor paths,
rich restart causes, argument fields and file fields. It also checks 15 explicit rejections,
three empty exception sentinels and three application-reader override results. Success is:

```text
[legacy-exceptions-native] OK reads=114 rejected=15 overrides=3 empty-sentinels=3
```

The registered JSON fixture callback recognizes exactly two byte strings. It tests ownership
of an application's explicitly registered reader; it is not a JSON parser or a proposed
production codec. The default historical JSON fields remain unsupported. In particular,
binding a new codec for future writes does not convert old id `1` JSON data.

This consumer preserves the old format's losses: `HResult` becomes `int.MinValue`, Data is
absent, HelpLink is empty, and already-decorated argument messages are decorated again by the
old serialization-constructor contract. It does not apply G8's richer state semantics to old
protobuf data. It establishes these codec reads, not Remote/Cluster startup or arbitrary
custom exception construction under Native AOT.

## Reproduce on Linux x64

Install SDK **10.0.114**. The runtime/compiler pack is pinned to **10.0.14** for this proof.
The script creates an app-local SDK selection directory inside its artifact output, so the
original base branch's older `global.json` cannot silently select SDK 10.0.101. This branch
now also includes the repository SDK servicing dependency [#8914](https://github.com/akkadotnet/akka.net/pull/8914). Point `AKKA_NATIVE_DOTNET` at a full dotnet CLI path if necessary:

```bash
AKKA_NATIVE_DOTNET=/path/to/dotnet \
  bash src/aot/Akka.Remote.LegacyException.AOT.App/reproduce.sh \
  /tmp/legacy-exception-proof
```

The script cleans this app's build graph, runs the focused managed tests and API tests,
publishes with app-local AOT settings, executes the native binary and compares visible
warnings. Use an isolated checkout and do not build the same output directories concurrently.
It adds no global command-line `PublishAot` property, warning suppression, package-audit bypass
or trimmer root. Ordinary solution builds remain managed.

## Validation summary

The corrected consumer passed native publish/run on SDK 10.0.114/runtime 10.0.14. Five existing Remote warning keys remained; no new, outside-scope or source-less diagnostics were observed. The earlier failed System native run preceded the corrected proof.

`baselines/previous-remote-warning-keys.txt` is a comparison-only copy of the six previously observed Remote keys, not an approved CI baseline. The corrected publish emits five existing keys; the former ExceptionSupport reflection key is no longer emitted.

`RemoteLegacyExceptionAotCanary` uses the shared
[`native-codec-canary.template.yaml`](../../../build-system/native-codec-canary.template.yaml)
to publish a cold ordinary consumer, require exit zero and
its exact 114-read/15-rejection/3-override/3-empty-sentinel marker, check scoped warnings and
retain logs even on failure. It also requires zero diagnostics without a source location.
The shared Remote baseline approves only the five existing keys retained above; no new
codec, consumer or V2 runtime warning is approved. Core warnings remain owned by its
separate rooted canary. Outside-scope counts are reported, not automatically rejected by
this lane; the local replay observed zero. The SDK servicing prerequisite is included.

The local replay passed publish, the exact runtime marker and warning checks on SDK 10.0.114/runtime 10.0.14. The final shared-template replay also passed its cold clean step, native run, exact marker and scoped/zero-unlocated warning assertions; it observed five existing warnings and zero new, outside-scope or unlocated diagnostics. These checks validate the commands, not hosted execution of the newly added Azure job.
