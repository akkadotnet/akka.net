---
uid: source-generated-serialization
title: Source-Generated MessagePack Serialization
---

# Source-Generated MessagePack Serialization

`Akka.Serialization.V2` is a NuGet package. It pairs a small runtime library with a Roslyn
incremental source generator. You mark your messages and one partial class with attributes. The
generator writes the rest: a `Manifest`/`Serialize`/`Deserialize`/`SizeHint` implementation backed
by [MessagePack](https://msgpack.org/), one write/read/size method per message type.

This page documents what ships on the `dev` branch today, ahead of the first Akka.NET v1.6 beta.
It does not document planned work. Planned work appears explicitly at the end. You can read this
page on GitHub at its full URL on `dev` even before the docs website publishes it.

## Overview

A classic Akka.NET serializer, `Serializer` or `SerializerWithStringManifest`, is code you write by
hand. You walk an object's fields yourself. You decide how to encode each one. The generator in
`Akka.Serialization.V2` takes the opposite approach. You declare the schema with attributes. It
writes the encoding code for you, at compile time. That buys four things:

* **A compile-time schema.** Every field is declared with `[AkkaField(index)]`. A typo, a missing
  field, or a duplicate index is a build error, not a runtime surprise.
* **No reflection.** Generated code calls `MessagePackWriter`/`MessagePackReader` directly. It
  never calls `PropertyInfo.GetValue`. It adds no boxing beyond what MessagePack itself needs. It
  never scans assemblies at runtime to find serializable types.
* **AOT- and trimming-friendly registration.** Serializers register through a generated static
  method, `CreateRegistration()`, not runtime type scanning. The generated serializer also
  satisfies Akka's classic `Serializer` reflection contract. So it works with HOCON registration
  too. See [Getting started](#getting-started).
* **Exact size hints.** `SizeHint` returns the exact byte count `Serialize` will write, or a
  well-known sentinel when it can't. Callers that pre-size a buffer skip the reallocation classic
  serializers pay for.

Use the source generator when you own the message types. Use it when you want compile-time
guarantees on the wire schema. Use it when you care about allocation-free, reflection-free
serialization, such as a hot message path in your own actors, or a subsystem like Akka.Remote's
Artery control-message serializer. See [Getting started](#getting-started). Reach for the classic
[`SerializerWithStringManifest`](xref:serialization) instead in three cases. The type is a
third-party type you cannot annotate, and it has no [hand-written formatter](#hand-written-formatters).
The payloads are genuinely open-ended, with no closed set of shapes. Or you are prototyping and do
not want to declare a schema up front.

## Getting started

### Package reference

The generator ships inside the `Akka.Serialization.V2` runtime package itself. It packs under
`analyzers/dotnet/cs`. NuGet and MSBuild auto-wire that path as a C# source generator for any
referencing project:

```xml
<PackageReference Include="Akka.Serialization.V2" Version="..." />
```

There is no separate generator package. There is no manual analyzer wiring. One reference gives you
the runtime types, `AkkaSerializer`, the attributes, and `IAkkaMessagePackFormatter<T>`, plus the
generator that reacts to them.

### The protocol interface

Every generated serializer is scoped to one **protocol**. A protocol is a marker interface. It
groups the message types the serializer dispatches at the top level.

```csharp
public interface IOrderBenchmarkProtocol
{
}
```

### A message

Mark a message type with `[AkkaSerializable]`. Give it a `Manifest`: a stable, serializer-owned
string that identifies the type on the wire. Index every field with `[AkkaField(n)]`:

```csharp
[AkkaSerializable(Manifest = "submit-order-v1")]
public sealed record SubmitOrder(
    [property: AkkaField(0)] string OrderId,
    [property: AkkaField(1)] Guid CustomerId,
    [property: AkkaField(2)] decimal Total,
    [property: AkkaField(3)] DateTimeOffset CreatedAt,
    [property: AkkaField(4)] IActorRef? ReplyTo) : IOrderBenchmarkProtocol;
```

(`SubmitOrder` and `IOrderBenchmarkProtocol` are trimmed fixtures from
`src/benchmark/Akka.Benchmarks/Serialization/GeneratedMessagePackSerializerBenchmarks.cs`.)

### The serializer class

Declare a `sealed partial class` that derives from `AkkaSerializer`. Annotate it with
`[AkkaSerializer<TProtocol>("name", id)]`. Declare a `static partial` `CreateRegistration()`
method. The generator fills in its body:

```csharp
[AkkaSerializer<IOrderBenchmarkProtocol>("order-benchmark", 120001)]
public sealed partial class OrderBenchmarkSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}
```

`Name` and `SerializerId` are both required, positional, and always explicit. There is no
auto-assigned id or alias. A generator's registration order across a compilation is
nondeterministic. A collision with an id or alias defined elsewhere, in HOCON, say, is invisible to
the generator at compile time. So it cannot safely default either value. Ids `1` through `100` are
reserved for Akka.NET's own built-in serializers. See the
[serializer id table](xref:serializer-codes). Pick something outside that range.

The generator emits the rest of the partial class in a `<ClassName>.AkkaSerialization.g.cs` file.
That file holds the constructor, `Identifier`, and the `Manifest`/`Serialize`/`Deserialize`/`SizeHint`
dispatch.

### Registering the serializer

`CreateRegistration()` returns a
[`SerializerRegistration`](xref:Akka.Serialization.V2.SerializerRegistration). It is an AOT-safe
value you compose explicitly. No runtime scanning is involved. Turn one registration into a
`SerializationSetup` with `CreateSetup()`:

```csharp
var setup = OrderBenchmarkSerializer.CreateRegistration().CreateSetup();
var bootstrap = BootstrapSetup.Create().And(setup);
var system = ActorSystem.Create("my-system", bootstrap);
```

To register several generated serializers at once, use the static
`SerializerRegistration.CreateSetup(params SerializerRegistration[])` overload:

```csharp
var setup = SerializerRegistration.CreateSetup(
    OrderBenchmarkSerializer.CreateRegistration(),
    EnvelopeBenchmarkSerializer.CreateRegistration());
```

Merge that `SerializationSetup` into `ActorSystemSetup` the same way as for a hand-written
serializer. See
[Configuring Serialization Bindings Programmatically](xref:serialization#configuring-serialization-bindings-programmatically).

### HOCON registration

A generated serializer derives from `AkkaSerializer`, then `SerializerV2`, then `Serializer`. So it
satisfies Akka's classic reflection contract: a public constructor that takes
`ExtendedActorSystem`, and a stable `Identifier`. You can register it exactly like any hand-written
serializer, through the classic `akka.actor.serializers` and `serialization-bindings` HOCON blocks.
Akka.Remote registers its own generated Artery control-message serializer this same way today, in
`Remote.conf`:

```hocon
akka.actor {
  serializers {
    artery-control = "Akka.Remote.Artery.ArteryControlMessageSerializer, Akka.Remote"
  }
  serialization-bindings {
    "Akka.Remote.Artery.IArteryControlMessage, Akka.Remote" = artery-control
  }
  serialization-identifiers {
    "Akka.Remote.Artery.ArteryControlMessageSerializer, Akka.Remote" = 23
  }
}
```

Nothing about this is generator-specific. It is the same three HOCON blocks the
[classic serializer documentation](xref:serialization#configuration) already describes. Use
`CreateRegistration()` and `CreateSetup()` when you want AOT-safe, compiler-checked composition.
Use HOCON when you follow an existing configuration-driven setup.

### Akka.Hosting

There is no `Akka.Hosting` integration for `Akka.Serialization.V2` yet. No extension method wires
generated registrations into an `AkkaConfigurationBuilder`. Compose registrations through
`SerializationSetup` and `ActorSystemSetup` as shown above, or through HOCON, until one ships.

## Messages and fields

### Field indexes are the wire contract

`[AkkaField(index)]` is the only thing that identifies a field on the wire. Declaration order does
not matter. Property name does not matter. Once a message ships with an index in use, two rules
apply. **Never renumber** it: a deserializer looks up the index, not the position. **Never reuse**
it for a different meaning, even after you remove the field it belonged to. A future reader might
still decode old bytes written under the old meaning. Indexes need not start at `0` or `1`. They
need not be contiguous. A message can legally use `2` and `10`, with nothing between:

```csharp
public sealed record SparseFieldMessage(
    [property: AkkaField(2)] int Quantity,
    [property: AkkaField(10)] string Name) : IGeneratedTestProtocol;
```

Renaming the C# property is always safe. The wire only ever sees the index.

### Nullable fields

A nullable value type, such as `int?`, `Guid?`, `DateTime?`, or a nullable enum, writes a
MessagePack nil when it is absent. A nullable reference type, such as `string?`, a nullable
collection, or a nullable nested message, does the same. Each writes its own encoding when present,
with no extra wrapper. The generator owns this nil encoding. A
[hand-written formatter](#hand-written-formatters) never sees the absent case.

### Records and init-only properties

The generator matches constructor parameters to `[AkkaField]` properties by name. It does not match
by declared position or field index. The match is case-insensitive. This is why the common C#
record shape works with zero extra ceremony:

```csharp
[AkkaSerializable(Manifest = "shipment-v1")]
public sealed record ShipmentMessage(
    [property: AkkaField(1)] string OrderId,
    [property: AkkaField(2)] ShippingAddress Address) : IGeneratedTestProtocol;
```

It also works for a plain class with `init`-only properties and no declared constructor. Every
`[AkkaField]` property is assigned through an object initializer:

```csharp
[AkkaSerializable(Manifest = "init-only-poco-v1")]
public sealed class InitOnlyPocoMessage : IGeneratedTestProtocol
{
    [AkkaField(1)] public string Name { get; init; } = string.Empty;
    [AkkaField(2)] public int Age { get; init; }
}
```

It also works for a hybrid shape. Some fields come from a hand-written constructor. Others are
assigned afterward through a settable property:

```csharp
[AkkaSerializable(Manifest = "mixed-shape-v1")]
public sealed class MixedShapeMessage : IGeneratedTestProtocol
{
    public MixedShapeMessage(string id, int quantity)
    {
        Id = id;
        Quantity = quantity;
    }

    [AkkaField(1)] public string Id { get; }
    [AkkaField(2)] public int Quantity { get; }
    [AkkaField(3)] public string? Notes { get; set; }
}
```

Three diagnostics guard this matching. **AKKASG026** fires when no constructor covers every
required `[AkkaField]` property: one that is non-nullable with no default. Then the type cannot be
reconstructed on deserialize. **AKKASG027**, a warning, fires when a defaulted constructor
parameter has no covering `[AkkaField]` property. It then silently resets to its default on every
deserialize. **AKKASG028** fires when an `[AkkaField]` property is not an accessible instance
property, such as a static property or a getter the generated code cannot reach.

### Structs

A `struct`, including a `readonly record struct`, can carry `[AkkaSerializable]` and `[AkkaField]`
properties exactly like a class. The generator writes it inline as a MessagePack map, with no
boxing wrapper:

```csharp
[AkkaSerializable]
public readonly record struct GapUniqueAddress(
    [property: AkkaField(1)] Address Address,
    [property: AkkaField(2)] long Uid);
```

### Fieldless messages and `AllowEmpty`

By default, an `[AkkaSerializable]` type with no `[AkkaField]` properties is rejected at compile
time (**AKKASG004**). This is almost always a forgotten `[AkkaField]`. Some protocol messages are
legitimately fieldless, though. A heartbeat's arrival *is* the signal, with nothing to carry. Opt
in with `AllowEmpty = true`:

```csharp
[AkkaSerializable(Manifest = "gap-heartbeat-v1", AllowEmpty = true)]
public sealed record GapHeartbeat : IGapFixProtocol;
```

This generates an empty-map write, a single `0x80` byte. It also generates a skip-loop read that
still tolerates unknown fields. A future sender can add fields to what this reader still treats as
fieldless.

### Nested `[AkkaSerializable]` value objects

A field can be typed as another `[AkkaSerializable]` type, with unlimited nesting depth. A
nested-only type never serves as a top-level protocol message. It needs no `Manifest`. Manifests
exist for top-level dispatch. A nested type is never looked up by manifest on its own:

```csharp
[AkkaSerializable(Manifest = "shipment-v1")]
public sealed record ShipmentMessage(
    [property: AkkaField(1)] string OrderId,
    [property: AkkaField(2)] ShippingAddress Address) : IGeneratedTestProtocol;

[AkkaSerializable]
public sealed record ShippingAddress(
    [property: AkkaField(1)] string Street,
    [property: AkkaField(2)] string City);
```

## Supported field types

| Kind | Types | Notes |
|---|---|---|
| Scalars | `string`, `int`, `long`, `bool`, `double`, `decimal`, `Guid`, `DateTime`, `DateTimeOffset` | Native `MessagePackWriter`/`Reader` calls; see [Wire format](#wire-format) for the encoding each one uses. |
| Raw bytes | `byte[]` | Encoded as a MessagePack `bin`, not an array of integers. |
| Enums | Any `enum` backed by `sbyte`, `byte`, `short`, `ushort`, or `int` | Written as an `int32`. A `long`- or `uint`-backed enum fails compilation with **AKKASG014**. |
| Nullable | `Nullable<T>` for any supported value type, plus nullable reference types | See [Nullable fields](#nullable-fields). |
| Arrays | `T[]`, including jagged arrays (`T[][]`) | `byte[]` is special-cased as raw bytes, not an array of `byte`. |
| Lists | `List<T>`, `IReadOnlyList<T>` | |
| Dictionaries | `Dictionary<TKey,TValue>`, `IReadOnlyDictionary<TKey,TValue>` | Key type is not restricted to `string`. |
| Read-only collections | `IReadOnlyCollection<T>` (for example a `ReadOnlyCollection<T>`) | |
| Immutable collections | `ImmutableArray<T>`, `ImmutableList<T>`, `ImmutableHashSet<T>`, `ImmutableDictionary<TKey,TValue>` | Every collection shape shares identical wire framing; only the in-memory construction on deserialize differs. |
| Actor references | `IActorRef` | Native support via `Serialization.SerializedActorPath` and `Provider.ResolveActorRef`. No formatter needed. |
| Addresses and paths | `Akka.Actor.Address`, `Akka.Actor.ActorPath` | Via the built-in `AddressFormatter` and `ActorPathFormatter`. See [Hand-written formatters](#hand-written-formatters). Not native, since `Akka.Actor` cannot reference `Akka.Serialization.V2`. |
| Nested messages | Any `[AkkaSerializable]` class or struct | See [Nested value objects](#nested-akkaserializable-value-objects). |
| Closed generic constructions | A `[AkkaSerializable]` generic type, registered per closed construction | See [Closed generic registrations](#closed-generic-registrations). |
| Unions | A closed, explicitly enumerated set of concrete types | See [Unions](#unions). |
| Envelope payloads | Any type, resolved through Akka's own serializer lookup at runtime | See [Envelope payloads](#envelope-payloads). |

**Not supported:** `float`, `single`, plain `byte`, `sbyte`, `short`, `ushort`, `uint`, and `ulong`
as scalar field types. These are only meaningful as an enum's underlying type. Also not supported:
a mutable `HashSet<T>` or `ISet<T>`. Use `ImmutableHashSet<T>` instead. Also not supported: a bare
`object` field with neither `[AkkaEnvelopePayload]` nor `[AkkaUnion]` applied. Any of these fails
compilation with **AKKASG003**, naming the offending property and type.

## Unions

`[AkkaUnion(typeof(A), typeof(B), ...)]` declares a **closed, explicitly enumerated** set of
concrete member types. It applies to an interface, an abstract class, or one specific field.
Unlike [envelope payloads](#envelope-payloads), a union is encoded structurally inline, at compile
time. No runtime serializer lookup is involved.

The natural declaration site is the union's base type. There, the member set is stated once. Every
field of that static type inherits it:

```csharp
[AkkaUnion(typeof(OrderPlaced), typeof(OrderCancelled), typeof(OrderNote))]
public interface IOrderEvent
{
}

[AkkaSerializable(Manifest = "union-envelope-v1")]
public sealed record UnionEnvelope(
    [property: AkkaField(1)] string EnvelopeId,
    [property: AkkaField(2)] IOrderEvent Event) : IUnionTestProtocol;

[AkkaSerializable(Manifest = "order-placed-v1")]
public sealed record OrderPlaced(
    [property: AkkaField(1)] string OrderId,
    [property: AkkaField(2)] int Quantity) : IOrderEvent, IUnionTestProtocol;

[AkkaSerializable(Manifest = "order-cancelled-v1")]
public record OrderCancelled(
    [property: AkkaField(1)] string OrderId,
    [property: AkkaField(2)] string Reason) : IOrderEvent;

[AkkaSerializable(Manifest = "order-note-v1")]
public readonly record struct OrderNote(
    [property: AkkaField(1)] string OrderId,
    [property: AkkaField(2)] string Note) : IOrderEvent;
```

`OrderPlaced` shows a union member that is also a top-level protocol message. It has one manifest
and two roles. `OrderNote` shows a value-type member, dispatched through boxing. A single field can
override the inherited member set with its own, narrower `[AkkaUnion]`:

```csharp
[AkkaSerializable(Manifest = "optional-union-v1")]
public sealed record OptionalUnionMessage(
    [property: AkkaField(1)] string Id,
    [property: AkkaField(2), AkkaUnion(typeof(OrderPlaced), typeof(OrderCancelled))]
    IOrderEvent? MaybeEvent) : IUnionTestProtocol;
```

### Member requirements

Every member type must meet four requirements. It must be **serializable**: an `[AkkaSerializable]`
class or struct handled by the same serializer (**AKKASG015**). It must be **manifested**, since
its manifest is the union's discriminator (**AKKASG016**). That manifest must be unique within the
union (**AKKASG017**). It must be **assignable** to the field's static type (**AKKASG018**). It
must be **concrete**. An abstract member is dead code, since dispatch matches the exact runtime
type, and an abstract type is never a runtime type (**AKKASG036**, warning). A merely unsealed
member is only advisory (**AKKASG025**, info). It works, but an undeclared subtype fails at
serialize time.

**AKKASG019** guards the member set itself, for example a duplicate member type. **AKKASG035**,
info, fires when a field carries both `[AkkaEnvelopePayload]` and `[AkkaUnion]`. The envelope
marker wins. The union declaration is ignored.

### Exact-runtime-type dispatch

Write dispatch matches the *exact* runtime type. It ignores the declared static type and any base
type. A value whose exact type is not a declared member fails with a `SerializationException`.
This includes an undeclared subtype of a declared member. The generator never silently widens the
value to the base type and loses state. On read, an unrecognized manifest inside the union frame
throws a `SerializationException` naming it. The caller decides what to do with either failure.
The generator never guesses.

## Closed generic registrations

A Roslyn source generator cannot reify an open generic type. It can only emit concrete code for
closed constructions it can see. So a generic `[AkkaSerializable]` type is never itself serialized.
The open definition exists only to host the `[AkkaField]` schema its closed constructions share:

```csharp
[AkkaSerializable]
public sealed record Wrapper<T>(
    [property: AkkaField(1)] string WrapperId,
    [property: AkkaField(2)] T Payload,
    [property: AkkaField(3)] int? Priority) : IClosedGenericTestProtocol;

[AkkaSerializer<IClosedGenericTestProtocol>("closed-generic-test", 120404)]
[AkkaSerializable<Wrapper<OrderRequest>>(Manifest = "wrap-request-v1")]
[AkkaSerializable<Wrapper<OrderReceipt>>(Manifest = "wrap-receipt-v1")]
public sealed partial class ClosedGenericTestSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}
```

Each `[AkkaSerializable<T>(Manifest = ...)]` registration behaves exactly like an ordinary
top-level message. It gets its own manifest and dispatch arm. Its generic fields resolve against
the concrete type arguments. This mirrors `System.Text.Json`'s source generator, which requires
`[JsonSerializable]` on each closed construction instead of accepting an unbound generic.

A combined shape also works: a generic wrapper whose payload field is itself a closed union.
`EventWrapper<T>` declares no union of its own. At construction time, when `T` is `IOrderEvent`,
the substituted type's own type-level `[AkkaUnion]` applies. The generator picks it up and
validates it against that type:

```csharp
[AkkaSerializable]
public sealed record EventWrapper<T>(
    [property: AkkaField(1)] string Id,
    [property: AkkaField(2)] T Body) : IClosedGenericTestProtocol;
```

The generator names each construction's generated Write, Read, and `SizeOf` methods by folding the
type arguments into the method name. So `Wrapper<int>` becomes `WriteWrapperInt`. And
`Wrapper<Pair<int, string>>` becomes `WriteWrapperPairIntString`. This is an implementation detail.
You never write these names yourself.

Diagnostics guard this shape. **AKKASG022** fires when a generic definition implements the
protocol with no closed registrations at all. **AKKASG023** fires when a field uses an
unregistered closed construction. **AKKASG020** and **AKKASG021** guard the registration itself,
catching an invalid or a duplicate one. **AKKASG037**, info, fires when a `Manifest` is set on the
*open* definition, where the generator ignores it. **AKKASG034** fires when a registered
construction implements no protocol and is unreachable from any field.

## Envelope payloads

`[AkkaEnvelopePayload]` marks a field as an **Akka serializer boundary**, not a structurally
encoded value. The generator does not generate inline MessagePack code for the field's static
type. Instead, it emits a runtime lookup. It asks the actor system's `Serialization` extension for
whatever serializer is bound to the payload's *actual* runtime type. It stores the result as a
`{serializer id, manifest, bytes}` triple.

Reach for an envelope payload instead of a union when the set of possible payload types is not
closed at compile time. A generic delivery wrapper is one example: its payload could be *any*
message type in the application, including ones the wrapper's own serializer never sees. A payload
already owned by a different serializer is another example, such as a classic
`SerializerWithStringManifest` or another generated serializer. Prefer a union whenever the payload
set is genuinely closed and known up front. A union is cheaper.

```csharp
[AkkaSerializable(Manifest = "benchmark-outer-envelope-v1")]
public sealed record BenchmarkOuterEnvelope(
    [property: AkkaField(0)] string EnvelopeId,
    [property: AkkaField(1), AkkaEnvelopePayload] BenchmarkInnerEnvelope Inner) : IEnvelopeBenchmarkProtocol;

[AkkaSerializable(Manifest = "benchmark-inner-envelope-v1")]
public sealed record BenchmarkInnerEnvelope(
    [property: AkkaField(0)] string EnvelopeId,
    [property: AkkaField(1), AkkaEnvelopePayload] object Payload) : IEnvelopeBenchmarkProtocol;
```

The wire frame is a 3-entry map: `{1: serializerId, 2: manifest, 3: bytes}`. This is deliberately
different from a union's `{1: manifest, 2: member fields}` frame. A union member is encoded inline
at compile time. An envelope payload instead goes through Akka's ordinary serializer lookup at
runtime. This is the same contract `Akka.Remote`'s `WrappedPayloadSupport` and
`Akka.Persistence`'s wrapper messages already use for opaque payloads.

That runtime lookup has a real cost. This is why an envelope payload is not the default choice. It
needs a serializer lookup on every write and read. It needs a staging buffer, since the inner
payload's byte length must be measured before the outer `bin` field is written. It can also report
an [`UnknownSize`](xref:Akka.Serialization.SerializerV2) hint, whenever the inner serializer cannot
report an exact size. Unknown size is transitive. One unknown-size payload makes every enclosing
serializer's `SizeHint` report unknown too.

Envelope payloads legitimately nest a level or two. A delivery message might wrap a user payload
that is itself enveloped. Unbounded nesting is only reachable when a message type declares itself,
directly or transitively, as its own envelope payload. That is an application bug. Left unchecked,
it recurses until the thread stack overflows, an uncatchable process kill. `AkkaSerializer` caps
nesting at 100 per call. Past that depth, it throws a catchable `SerializationException` reporting
it exceeded the maximum depth. This happens on write, size, and read alike. It matches the
recursion limit `Google.Protobuf` already enforces. See `EnvelopeDepthGuardSpec` for the full
behavior. That spec also proves the depth counter always unwinds after a failure.

## Hand-written formatters

Some types cannot be annotated with `[AkkaSerializable]`. A core Akka type like
`Akka.Actor.Address` is the most common case. It lives in an assembly that cannot reference
`Akka.Serialization.V2` without creating a dependency cycle. Apply
`[AkkaSerializerFormatter<TTarget, TFormatter>]` to the `[AkkaSerializer]` class instead. It routes
every field of type `TTarget` through a hand-written `IAkkaMessagePackFormatter<TTarget>`
implementation:

```csharp
public interface IAkkaMessagePackFormatter<T>
{
    void Write(ref MessagePackWriter writer, T value);
    T Read(ref MessagePackReader reader);
    int SizeOf(T value);
}
```

The contract has four rules. `Write` and `Read` must be symmetric, reproducing an equivalent
value. `Write` must produce exactly **one** top-level MessagePack value. Wrap several values in a
single array or map instead. The generated map framing and the unknown-field skip path both depend
on one field id mapping to one value. `value` is never the absent case for a non-nullable field.
The generator, not the formatter, owns nil encoding for nullable fields. It only calls the
formatter for present values. A `Nullable<T>` field matches a formatter registered for the
underlying `T`. `SizeOf` must return the *exact* encoded byte count, or `SerializerV2.UnknownSize`
when that is not cheap to compute. An incorrect non-negative value silently corrupts the enclosing
`SizeHint` contract.

A formatter needs a public parameterless constructor, or a public constructor that takes
`ExtendedActorSystem`. When both exist, the generator prefers the latter. A formatter only declares
that constructor for system context. Having neither usable constructor fails compilation
(**AKKASG010**).

A custom formatter can compose a built-in one. Here's one that wraps `AddressFormatter` to encode
a `(Address, long)` pair:

```csharp
public sealed class TestUniqueAddressFormatter : IAkkaMessagePackFormatter<TestUniqueAddress>
{
    private readonly AddressFormatter _addressFormatter = new();

    public void Write(ref MessagePackWriter writer, TestUniqueAddress value)
    {
        writer.WriteArrayHeader(2);
        _addressFormatter.Write(ref writer, value.Address);
        writer.Write(value.Uid);
    }

    public TestUniqueAddress Read(ref MessagePackReader reader)
    {
        var length = reader.ReadArrayHeader();
        if (length != 2)
            throw new SerializationException($"Expected a 2-element unique-address array, got {length}.");

        var address = _addressFormatter.Read(ref reader);
        var uid = reader.ReadInt64();
        return new TestUniqueAddress(address, uid);
    }

    public int SizeOf(TestUniqueAddress value)
    {
        var addressSize = _addressFormatter.SizeOf(value.Address);
        if (addressSize < 0)
            return SerializerV2.UnknownSize;

        return MessagePackSizes.SizeOfArrayHeader(2) + addressSize + MessagePackSizes.SizeOfInt64(value.Uid);
    }
}
```

`MessagePackSizes` is the same public, static exact-size math the generated serializers use
internally. Compose it in your own formatters instead of hand-deriving header sizes.

Register a formatter on the serializer class alongside `[AkkaSerializer<TProtocol>]`:

```csharp
[AkkaSerializer<IControlMirrorProtocol>("control-mirror", 120102)]
[AkkaSerializerFormatter<Address, AddressFormatter>]
[AkkaSerializerFormatter<TestUniqueAddress, TestUniqueAddressFormatter>]
[AkkaSerializerFormatter<ActorPath, ActorPathFormatter>]
public sealed partial class ControlMirrorSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}
```

### The two built-in formatters

`Akka.Serialization.V2` ships two formatters. They let `Address` and `ActorPath` fields work out
of the box. `AddressFormatter` writes `Address` as a 4-element array:
`[Protocol, System, Host-or-nil, Port-or-nil]`. This is byte-identical to the wire format
`ArteryControlMessageSerializer`'s hand-rolled `WriteAddress` already produces. So a generated
serializer that registers it interoperates with that format today. `ActorPathFormatter` writes a
single transport-aware string. It follows the same convention the generator applies to native
`IActorRef` fields. It uses the thread-static transport context when one is set. Otherwise it uses
the owning system's default address, when constructed with an `ExtendedActorSystem`. Otherwise it
uses the path's own address.

## Wire format

Every generated message writes a MessagePack **map**. Its keys are the field indexes from
`[AkkaField]`, not an array in declaration order. A reader iterates the map's entries and switches
on the field id. It calls `reader.Skip()` for any id it does not recognize. This is the mechanism
behind [forward-compatible upgrades](#versioning-and-rolling-upgrades).

Here is `SparseFieldMessage(17, "alpha")` as an annotated hex dump. Its fields sit at indexes `2`
and `10`. The dump comes from the project's committed wire-format snapshot corpus:

```text
case: sparse-field-indices
message-type: SparseFieldMessage
manifest: sparse-v1
serializer-id: 120101
byte-count: 10

0000  82 02 11 0a a5 61 6c 70  68 61                    |.....alpha|
```

`0x82` is a 2-entry map header. `02`/`11` is field `2`, value `17`, a fixint. `0a`/`a5 61 6c 70 68
61` is field `10`, value `"alpha"`, a 5-byte fixstr. The entries happen to appear in index order
here. Nothing about the format requires that.

Every write and read pair the generator emits follows the same shape. This example is trimmed from
a generated serializer's golden output, for a message with one required `string` field:

```csharp
private void WriteMiniMessage(ref MessagePackWriter writer, MiniMessage message)
{
    writer.WriteMapHeader(1);
    writer.Write(1);
    writer.Write(message.Text);
}

private MiniMessage ReadMiniMessage(ref MessagePackReader reader)
{
    var fieldCount = reader.ReadMapHeader();
    string? text = null;
    var hasText = false;
    for (var entryIndex = 0; entryIndex < fieldCount; entryIndex++)
    {
        var fieldId = reader.ReadInt32();
        switch (fieldId)
        {
            case 1:
                text = reader.ReadString();
                hasText = true;
                break;
            default:
                reader.Skip();
                break;
        }
    }

    if (!hasText || text is null)
        throw new SerializationException("Missing required field [Text] with index [1] while deserializing [MiniMessage].");
    return new MiniMessage(Text: text!);
}
```

## Versioning and rolling upgrades

Two separate rules cover the two ways a message shape can change during a rolling upgrade.

**Adding a field to an existing message.** Make it nullable. Older readers run code that predates
the field. They skip the unknown key, using the `default: reader.Skip()` branch shown above, and
never see it. Newer code might read a message an old node wrote. It sees the field simply absent.
A nullable field deserializes that as `null` instead of throwing. A **non-nullable** new field
breaks this. An old message, read by code that now expects the field, throws
`"Missing required field [Name] with index [1] while deserializing [...]."` This is why a new
field must be nullable, not the other way around.

**Adding a new message type or a new union member.** Deploy the code that can *read* it to every
node before any node *sends* it. The same rule covers writes for a persisted message, one written
to Akka.Persistence journals or snapshots. Deploy everywhere before any node *writes* the new
shape. A persisted event or snapshot is a durable contract. Whatever code runs later reads it back.

Manifests are part of that wire contract in both cases. A generated serializer's `Manifest` is a
stable, serializer-owned token. It stays stable across calls and across fresh instances. It is
never the CLR type's name. Changing it is exactly as breaking as changing a field index.

## Diagnostics reference

Every id below is a `DiagnosticDescriptor` in `AkkaSerializerGenerator.cs`. **AKKASG030** does not
exist. The C# compiler itself already rejects duplicate `[AkkaSerializer<T>]` attributes on one
class, as `CS0579`: "Duplicate attribute". So the generator never needs its own diagnostic for
that case.

| Id | Severity | Title | Meaning |
|---|---|---|---|
| AKKASG001 | Error | Serializer name must be a non-empty string | The `Name` argument to `[AkkaSerializer<T>]` is null, empty, or whitespace. |
| AKKASG002 | Error | Serializer id must be a positive integer | The `SerializerId` argument is zero or negative. |
| AKKASG003 | Error | Unsupported field type | An `[AkkaField]` property's type isn't one the generator can encode. |
| AKKASG004 | Error | No serializable fields | An `[AkkaSerializable]` type has no `[AkkaField]` properties and didn't opt in with `AllowEmpty`. |
| AKKASG005 | Error | Duplicate field index | Two `[AkkaField]` properties on the same type share an index. |
| AKKASG006 | Error | Top-level message manifest is required | A type implementing the serializer's protocol has no `Manifest`. |
| AKKASG007 | Error | Nested value object serialization definition is required | A nested field's type isn't `[AkkaSerializable]` with its own `[AkkaField]`s. |
| AKKASG008 | Error | Formatter type must not be abstract | A registered `TFormatter` is abstract and can't be instantiated. |
| AKKASG009 | Error | Duplicate formatter registration | A serializer registers more than one formatter for the same target type. |
| AKKASG010 | Error | Formatter constructor not usable | A formatter has neither a public parameterless constructor nor one taking `ExtendedActorSystem`. |
| AKKASG011 | Error | Formatter target type is not supported | A formatter's target type is generic, an array, or otherwise not a plain named type. |
| AKKASG012 | Error | Duplicate top-level message manifest | Two top-level messages on the same serializer share a manifest. |
| AKKASG013 | Error | Duplicate serializer id | Two `[AkkaSerializer]` classes in the compilation declare the same `SerializerId`. |
| AKKASG014 | Error | Enum underlying type is not supported | An enum field's underlying type isn't fully `int32`-representable (for example `long` or `uint`). |
| AKKASG015 | Error | Union member type is not serializable | A declared union member isn't an `[AkkaSerializable]` type handled by this serializer. |
| AKKASG016 | Error | Union member manifest is required | A union member has no `Manifest`, so it can't act as the union's discriminator. |
| AKKASG017 | Error | Union member manifests must be unique | Two members of the same union share a manifest. |
| AKKASG018 | Error | Union member is not assignable to the field type | A declared member isn't implicitly convertible to the field's static type. |
| AKKASG019 | Error | Union member set is invalid | The union's member set itself is malformed (for example, a duplicate member type). |
| AKKASG020 | Error | Closed generic registration is invalid | An `[AkkaSerializable<T>]` registration isn't a closed construction of a generic `[AkkaSerializable]` type. |
| AKKASG021 | Error | Duplicate closed generic registration | The same closed construction is registered more than once on one serializer. |
| AKKASG022 | Error | Generic serializable type requires closed generic registrations | A generic `[AkkaSerializable]` type implements the protocol but has no closed registrations. |
| AKKASG023 | Error | Closed generic field type is not registered | A field uses a closed generic construction that was never registered with `[AkkaSerializable<T>]`. |
| AKKASG024 | Error | Generated member name collision | Two distinct message types would produce the same generated member name. |
| AKKASG025 | Info | Union member type is not sealed | An undeclared subtype of this (concrete, non-sealed) member will fail serialization if it's ever sent. |
| AKKASG026 | Error | No matching constructor | No constructor covers every required `[AkkaField]` property, so the type can't be reconstructed. |
| AKKASG027 | Warning | Constructor parameter not covered by [AkkaField] | A defaulted constructor parameter has no matching `[AkkaField]` property and silently resets on every deserialize. |
| AKKASG028 | Error | [AkkaField] must be on an accessible instance property | The property is static, or otherwise unreachable from the generated code. |
| AKKASG029 | Error | Protocol message type is not [AkkaSerializable] | A type implements the serializer's protocol but isn't `[AkkaSerializable]`, so it's invisible to the generated dispatch. |
| AKKASG031 | Error | Protocol interface bound by multiple serializers | Two `[AkkaSerializer]` classes bind the same protocol interface. |
| AKKASG032 | Error | Serializer class shape is invalid | The `[AkkaSerializer]` class isn't `partial`, is generic, or doesn't derive from `AkkaSerializer`. |
| AKKASG033 | Error | Protocol type must be an interface | The `TProtocol` type argument to `[AkkaSerializer<TProtocol>]` isn't an interface. |
| AKKASG034 | Error | Registered closed generic type does not implement the serializer protocol | A closed generic registration implements no protocol and is unreachable from any field, so it has no effect. |
| AKKASG035 | Info | Union declaration is ignored on an envelope payload field | A field has both `[AkkaEnvelopePayload]` and `[AkkaUnion]`; the envelope marker wins. |
| AKKASG036 | Warning | Union member type is abstract | An abstract union member can never be the exact runtime type, so its dispatch branch is dead code. |
| AKKASG037 | Info | Manifest on a generic [AkkaSerializable] definition is ignored | A `Manifest` set on the *open* generic definition is ignored; only closed constructions carry one. |

## Limitations today and planned changes

The generator is syntax-driven. It discovers `[AkkaSerializable]`, `[AkkaSerializer<T>]`, and
protocol-implementing types by walking the current compilation's own syntax trees. Three concrete
consequences follow today. First, a type used as a nested field or a union member must be declared
in the same compilation as the serializer that uses it. One exception applies: a closed generic's
own open definition may live in a referenced assembly. Only its registration,
`[AkkaSerializable<T>]`, must stay local to the serializer's compilation. Second, the generator
discovers top-level protocol messages only within the serializer's own compilation. A type in a
referenced assembly might implement the protocol interface. If this generator run never saw it, it
stays invisible. Third, a construction built through reflection over a type the generator never
saw at compile time fails only when it is first sent. This never happens at compile time. A
generic instantiation assembled dynamically at runtime is one example.

The following work is planned. None of it ships on `dev` today. It is tracked against
[issue #8384](https://github.com/akkadotnet/akka.net/issues/8384) and
`openspec/changes/messagepack-sourcegen-validation/design.md`:

* **Schema extraction from referenced-assembly metadata.** The generator will read a type's
  `[AkkaField]` schema from a referenced assembly's compiled metadata. The type's declaration
  syntax will no longer need to be in the current compilation.
* **Expansion of a registration over a closed set with a `ManifestPrefix`.** One registration
  will cover many closed constructions from a declared set. Today each construction needs its own
  `[AkkaSerializable<T>]` attribute.
* **Discovery of protocol implementors in referenced assemblies.** The generator will find types
  that implement a serializer's protocol interface across assembly boundaries. Today it looks only
  in the current compilation.
* **Removal of `[AkkaEnvelopePayload]`.** This attribute is planned for removal before the first
  1.6 beta. A property typed `object` will then be the serializer boundary on its own. An
  interface-typed property will need a closed set, `[AkkaUnion]` or the protocol interface, or it
  must be retyped to `object`.
