---
uid: exception-messagepack-formatter
title: Exception fields in generated serializers
---

`Akka.Remote.Serialization.ExceptionMessagePackFormatter` handles `Exception` fields in a generated
serializer. Reference `Akka.Remote` and register the formatter on the serializer that owns your
messages:

```csharp
public interface IFailureProtocol { }

[AkkaSerializable(Manifest = "failure-v1")]
public sealed record Failure([property: AkkaField(1)] Exception? Cause) : IFailureProtocol;

[AkkaSerializer<IFailureProtocol>("failures", 120051)]
[AkkaSerializerFormatter<Exception, ExceptionMessagePackFormatter>]
public sealed partial class FailureSerializer : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}
```

The generator supplies the `ExtendedActorSystem` to the formatter. Nullable fields retain nil.
This API is **Unreleased**. It adds no global exception serializer, alias, configuration switch,
or change to the existing Remote serializers.

The native path uses 36 exact exception types: twelve BCL types and the nineteen simple plus five
rich Akka/Remote exceptions covered by `ExceptionSupportSpec`. The rich types are
`ActorInitializationException`, `DeathPactException`, `PreRestartException`, `PostRestartException`,
and `OpenCircuitException`. Their actor refs, restart/original causes, optional message and duration
survive the round trip. Raw serialization-constructor state preserves messages, stack/source,
help links, HResult and data entries. Null/NoSender and Nobody remain distinct.

An exception type being supported does not make arbitrary values in `Data`, `OptionalMessage`,
or other payload fields Native AOT compatible. Register a native serializer for those values.
With dynamic loading disabled, unknown exception types and payloads that would use the default
reflection-based JSON serializer fail before this formatter writes caller output.

On the JIT, with dynamic loading enabled, custom `ISerializable` exceptions retain their declared
field types and serialization constructors, including explicit interface implementations and
forwarded type names. An unknown native descriptor never falls back to loading a type by name.

The formatter stages one complete exception value before writing it. A containing serializer that
promises no partial output for the **whole message** must prepare its other fields or stage the
whole message too. Registering this formatter alone does not make that broader promise.

The fragment's field ids, value tags and exception descriptors are permanent wire identities.
Descriptor `0` is the guarded JIT form; `1`–`12` identify BCL factories, `100`–`118` identify simple
Akka/Remote factories, and `119`–`123` identify the five rich factories in the order listed above.
New factories append unused descriptors; existing numbers and field meanings must never be
renumbered or reused. The private generated fragment codec is not a registered serializer row.
