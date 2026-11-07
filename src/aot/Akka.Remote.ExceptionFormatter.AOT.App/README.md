# Exception formatter native consumer

This ordinary consumer uses public exceptions and `ExceptionMessagePackFormatter` through a
generated user codec. It has no friend assembly identity, private product DTO access or blanket
trimmer roots. It asserts that dynamic code is unavailable and dynamic type loading is disabled.

Publish using the serviced SDK 10.0.114 (compiler/runtime 10.0.14), then run the native executable:

```bash
dotnet publish src/aot/Akka.Remote.ExceptionFormatter.AOT.App -c Release -r linux-x64 -o artifacts/g8-native
./artifacts/g8-native/Akka.Remote.ExceptionFormatter.AOT.App
```

The consumer verifies all 36 factories through generated formatter calls, raw/rich exception
state, nullable fields, a registered native user payload, actor-ref sentinels and six unknown-state
or descriptor rejections. Four write rejections also require an empty caller buffer. A log watchdog
fails on startup, serialization or shutdown warnings/errors instead of accepting an empty boot.

Expected success output includes:

```text
[g8-native] known-factories=36 verified
[g8-native] OK known=36 rich=3 null-field=1 native-user-payload=true rejected=6
```

ILC diagnostics remain visible, matching the existing canary policy. Local publication reported
seven existing Remote warnings in ProtobufSerializer, FailureDetectorLoader, DaemonMsgCreateSerializer,
EndpointManager, ExceptionSupport and TransportAdapters. No new formatter/factory/codec diagnostics
were reported. This consumer proves these exception codec paths, not full Remote or native transport
compatibility. System-message and Misc serializer registrations remain separate changes.

The `evidence` directory retains the actual publish/run output and exact local toolchain commands.
