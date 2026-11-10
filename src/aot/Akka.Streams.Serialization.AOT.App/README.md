# Streams protocol native consumer

This ordinary consumer obtains private protocol messages from twelve frozen protobuf records
captured from the unchanged public `StreamRefSerializer` (source blob
`49b1656555b795a1964fb2cbb4642d9c05427e66`). The paired MessagePack records are reviewed
fixtures. No friend assembly name, private type access or blanket assembly roots are used.
The Remote reference supplies the independently registered primitive payload serializer 17;
this is not a native transport or remote stream materialization test.

Run from the repository with serviced SDK 10.0.403 / native compiler and runtime 10.0.14:

```bash
dotnet publish src/aot/Akka.Streams.Serialization.AOT.App/StreamsNativeProbe.csproj \
  -c Release -r linux-x64 -o /tmp/streams-native-publish \
  > /tmp/streams-native-publish.log 2>&1
timeout 120 /tmp/streams-native-publish/StreamsNativeProbe

dotnet run scripts/CheckAotWarnings.cs -- \
  --log /tmp/streams-native-publish.log \
  --baseline src/aot/Akka.Streams.Serialization.AOT.App/aot-warnings.baseline.txt \
  --scope "src/core/Akka.Streams/,src/core/Akka.Serialization.V2/,src/aot/Akka.Streams.Serialization.AOT.App/" \
  --repo-root .
```

The executable requires native execution and `Akka.DynamicTypeLoading=false`; it must print
`[streams-native] OK cases=36 native-writes=14 segmented-reads=36 user-payloads=6 rejected=12` and exit zero.
It checks default/off, global/on and an explicit pin of all three binding types, both frozen readers, every sequence
split, exact native writer counts, nested primitive and independently registered V1/V2 user payloads, and empty/unresolved handshake
canonicalization. Valid frozen bare-ref E/F records for both ids 30 and 70 must reject with
the `Akka.DynamicTypeLoading` reason. Id 70 supports all seven forms on JIT; its E/F
reads and writes explicitly reject with dynamic loading disabled. Typed field support remains
separate #8673 work. Existing Remote warnings remain visible and are not waived by the
empty Streams/probe baseline. Complete subsystem Native AOT support is not established.
