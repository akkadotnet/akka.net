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
base branch's older `global.json` cannot silently select SDK 10.0.101. The repository SDK pin
is unchanged. Point `AKKA_NATIVE_DOTNET` at a full dotnet CLI path if necessary:

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

## Retained receipts

The [`evidence`](evidence) directory contains compressed full command output, original failed
native output, and exact commands. `gzip -dc evidence/<name>.log.gz` displays a receipt.
The publish/run logs retain their original machine paths for provenance; reproduction uses
the relative project references and the script above.

`previous-remote-warning-keys.txt` is a comparison-only copy of the six previously observed
Remote keys, not a newly approved CI baseline. Five existing keys remain in the corrected
publish; the old ExceptionSupport reflection key is no longer emitted. No new, outside-scope
or source-less diagnostics were observed. The original failed System native receipt remains
retained even though the corrected consumer passes.
