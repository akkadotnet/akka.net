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
  method, `CreateSerializerDetails(system)`, not runtime type scanning. The generated serializer also
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

## Getting Started

### Package Reference

The generator ships inside the `Akka.Serialization.V2` runtime package itself. It packs under
`analyzers/dotnet/cs`. NuGet and MSBuild auto-wire that path as a C# source generator for any
referencing project:

```xml
<PackageReference Include="Akka.Serialization.V2" Version="..." />
```

There is no separate generator package. There is no manual analyzer wiring. One reference gives you
the runtime types, `AkkaSerializer`, the attributes, and `IAkkaMessagePackFormatter<T>`, plus the
generator that reacts to them.

### The Protocol Interface

Every generated serializer is scoped to one **protocol**. A protocol is a marker interface. It
groups the message types the serializer dispatches at the top level.

```csharp
public interface IOrderBenchmarkProtocol
{
}
```

### A Message

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

> [!NOTE]
> On positional records, `property:` is required: `[property: AkkaField(n)]` annotates the
> generated property. Without it, C# targets the constructor parameter and rejects the attribute.
> See [Records and Init-only Properties](#records-and-init-only-properties) for an example.

### The Serializer Class

Declare a `sealed partial class` that derives from `AkkaSerializer`. Annotate it with
`[AkkaSerializer<TProtocol>("name", id)]`. Declare a `static partial` `CreateSerializerDetails(system)`
method. The generator fills in its body:

```csharp
[AkkaSerializer<IOrderBenchmarkProtocol>("order-benchmark", 120001)]
public sealed partial class OrderBenchmarkSerializer : AkkaSerializer
{
    public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system);
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
dispatch. To add a message that doesn't implement `TProtocol`, see
[Including Messages Outside the Protocol](#including-messages-outside-the-protocol).

### Registering the Serializer

`CreateSerializerDetails(ExtendedActorSystem)` returns core
[`SerializerDetails`](xref:Akka.Serialization.SerializerDetails): the serializer instance, alias,
and message bindings. The generated factory is AOT-safe and uses no runtime scanning.

With Akka.Hosting, pass the generated method directly:

```csharp
builder.WithCustomSerializer(OrderBenchmarkSerializer.CreateSerializerDetails);
```

Without Hosting, compose one or more generated factories through core `SerializationSetup`:

```csharp
var setup = SerializationSetup.Create(extendedSystem => ImmutableHashSet.Create(
    OrderBenchmarkSerializer.CreateSerializerDetails(extendedSystem),
    EnvelopeBenchmarkSerializer.CreateSerializerDetails(extendedSystem)));
var bootstrap = BootstrapSetup.Create().And(setup);
var system = ActorSystem.Create("my-system", bootstrap);
```

Import `Akka.Serialization` and `System.Collections.Immutable` for this setup. See
[Configuring Serialization Bindings Programmatically](xref:serialization#configuring-serialization-bindings-programmatically).

### HOCON Registration

A generated serializer derives from `AkkaSerializer`, then `SerializerV2`, then
`SerializerWithStringManifest`, then `Serializer`. So it satisfies Akka's classic reflection
contract: a public constructor that takes `ExtendedActorSystem`, and a stable `Identifier`. You can register it exactly like any hand-written
serializer, through the classic `akka.actor.serializers` and `serialization-bindings` HOCON blocks:

```hocon
akka.actor {
  serializers {
    order-benchmark = "MyApp.OrderBenchmarkSerializer, MyApp"
  }
  serialization-bindings {
    "MyApp.IOrderBenchmarkProtocol, MyApp" = order-benchmark
  }
}
```

The generated serializer emits its own `Identifier` in code, from the id in `[AkkaSerializer<T>]`, so
it needs no `akka.actor.serialization-identifiers` row. A row for it would be ignored.

Nothing about this is generator-specific. It is the same two HOCON blocks the
[classic serializer documentation](xref:serialization#configuration) already describes. Use
`CreateSerializerDetails(system)` and `SerializationSetup.Create` when you want AOT-safe, compiler-checked composition.
Use HOCON when you follow an existing configuration-driven setup.

### Akka.Hosting

`Akka.Hosting` wires generated registrations into an `AkkaConfigurationBuilder` through
`WithCustomSerializer`, as shown above. Without Hosting, compose registrations through
`SerializationSetup` and `ActorSystemSetup`, or through HOCON.

## Messages and Fields

### Field Indexes Are the Wire Contract

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

### Nullable Fields

A nullable value type, such as `int?`, `Guid?`, `DateTime?`, or a nullable enum, writes a
MessagePack nil when it is absent. A nullable reference type, such as `string?`, a nullable
collection, or a nullable nested message, does the same. Each writes its own encoding when present,
with no extra wrapper. The generator owns this nil encoding. A
[hand-written formatter](#hand-written-formatters) never sees the absent case.

### Records and Init-only Properties

The generator matches constructor parameters to `[AkkaField]` properties by name. It does not match
by declared position or field index. The match is case-insensitive. This is why the common C#
record shape works with zero extra ceremony:

```csharp
[AkkaSerializable(Manifest = "shipment-v1")]
public sealed record ShipmentMessage(
    [property: AkkaField(1)] string OrderId,
    [property: AkkaField(2)] ShippingAddress Address) : IGeneratedTestProtocol;
```

> [!NOTE]
> **Record attribute targets:** Use `[property: AkkaField(n)]` on positional parameters of
> record classes and record structs. Without `property:`, C# targets the constructor parameter
> and reports that `AkkaField` is only valid on properties. This is a C# compiler error, not a
> source-generator diagnostic.

For example, a strongly typed session identity can retain its positional declaration, conversion
operator, and `ToString()` override:

[!code-csharp[SessionId](../../../src/core/Akka.Serialization.V2.Tests/FieldlessAndStructFieldSpec.cs?name=SessionId)]

`[AkkaSerializable]` opts this nested value type into the generator. The separate `[Serializable]`
attribute can remain, but does not opt a type into source-generated serialization on its own.

Ordinary class primary constructors do not generate properties. Declare a property explicitly,
initialize it from the constructor parameter, and apply `[AkkaField(n)]` to that property.

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

The same reach rule applies to the message type itself. The generated serializer is a separate
class, so it cannot name a type declared `private` or `protected`, or nested inside one. A message
nested in another class must be `internal` or `public`, and so must every type that contains it.
**AKKASG045** reports a violation at the type declaration.

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

Public auto-properties and positional record properties without `[AkkaField]` produce
AKKASG046 at the property declaration, even without a serializer. Such properties are not
serialized. Constructors or initializers can restore their initial values during deserialization.
Add `[AkkaField(n)]` to preserve the state, or use standard diagnostic suppression for an
intentional omission. `AllowEmpty` permits fieldless messages but does not suppress AKKASG046.
Computed properties and public fields are outside this diagnostic. `[AkkaField]` supports
properties only.

Type-shape checks run as soon as you mark a type with `[AkkaSerializable]`, even before
its serializer exists. Missing fields, duplicate indices, inaccessible marked getters, and
inaccessible declarations report once at their source declarations. The same checks apply to
open generic definitions. Checks that depend on a closed construction or serializer configuration
continue to report in that context.

### Fieldless Messages and `AllowEmpty`

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

### Nested `[AkkaSerializable]` Value Objects

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

## Supported Field Types

| Kind | Types | Notes |
|---|---|---|
| Scalars | `string`, `int`, `long`, `bool`, `double`, `float`, `short`, `byte`, `sbyte`, `ushort`, `uint`, `ulong`, `char`, `decimal`, `Guid`, `DateTime`, `DateTimeOffset`, `TimeSpan` | Native `MessagePackWriter`/`Reader` calls; see [Scalar encodings](#scalar-encodings) for the encoding each one uses. |
| Raw bytes | `byte[]` | Encoded as a MessagePack `bin`, not an array of integers. |
| Enums | Any `enum` backed by `sbyte`, `byte`, `short`, `ushort`, or `int` | Written as an `int32`. A `long`- or `uint`-backed enum fails compilation with **AKKASG014**. |
| Nullable | `Nullable<T>` for any supported value type, plus nullable reference types | See [Nullable fields](#nullable-fields). |
| Arrays | `T[]`, including jagged arrays (`T[][]`) | `byte[]` is special-cased as raw bytes, not an array of `byte`. |
| Lists | `List<T>`, `IReadOnlyList<T>` | |
| Dictionaries | `Dictionary<TKey,TValue>`, `IReadOnlyDictionary<TKey,TValue>` | Key type is not restricted to `string`. |
| Read-only collections | `IReadOnlyCollection<T>` (for example a `ReadOnlyCollection<T>`) | |
| Immutable collections | `ImmutableArray<T>`, `ImmutableList<T>`, `ImmutableHashSet<T>`, `ImmutableDictionary<TKey,TValue>` | Every collection shape shares identical wire framing; only the in-memory construction on deserialize differs. |
| Actor references | `IActorRef` | Native support via `Serialization.SerializedActorPath` and `Provider.ResolveActorRef`. No formatter needed. |
| Addresses and paths | `Akka.Actor.Address`, `Akka.Actor.ActorPath` | Via the built-in `AddressFormatter` and `ActorPathFormatter`, as fields or inside any collection above. See [Hand-written formatters](#hand-written-formatters). Not native, since `Akka.Actor` cannot reference `Akka.Serialization.V2`. |
| Nested messages | Any `[AkkaSerializable]` class or struct | See [Nested value objects](#nested-akkaserializable-value-objects). |
| Closed generic constructions | A `[AkkaSerializable]` generic type, registered per closed construction | See [Closed generic registrations](#closed-generic-registrations). |
| Unions | A closed, explicitly enumerated set of concrete types | See [Unions](#unions). |
| Envelope payloads | A property typed `object` or `object?`, resolved through Akka's own serializer lookup at runtime | See [Envelope payloads](#envelope-payloads). |

**Not supported:** a mutable `HashSet<T>` or `ISet<T>`. Use `ImmutableHashSet<T>` instead. Also not
supported: a dictionary whose key type is `object`. Any of these fails compilation with
**AKKASG003**, naming the offending property and type. So does a collection of a type that neither
the generator nor a registered [formatter](#hand-written-formatters) handles. A foreign type that
has no support at all, such as `Uri` or `Half`, fails with **AKKASG007**. Register a formatter for it.

### Scalar Encodings

| Type | Wire encoding |
|---|---|
| `string`, `bool`, `int`, `long` | MessagePack `str`, `bool`, and the smallest MessagePack integer that holds the value. |
| `double` | MessagePack `float64`. |
| `float` | MessagePack `float32`. |
| `short`, `sbyte`, `byte`, `ushort`, `uint`, `ulong` | MessagePack integer, in the smallest encoding that holds the value, exactly as `int` and `long` are written. |
| `char` | MessagePack unsigned integer of the character's UTF-16 code unit. |
| `TimeSpan` | MessagePack integer (`int64`) of its `Ticks`. |

Every one of these works as a field, as a `Nullable<T>` field, and as a collection element,
dictionary key, or dictionary value. A nullable value that is absent is MessagePack `nil`; a present
one is written exactly as the non-nullable type. Integers are read by value, not by width, so a peer
may write the same number with a different MessagePack integer encoding. A value the target type
cannot hold, such as `70000` read into a `short` or `-1` read into a `ulong`, throws a
`MessagePackSerializationException` that names the type. It is never truncated.

## Unions

A property typed as an interface, an abstract class, a closed hierarchy, or a C# 15 `union` is a
**union field**. The generator works out its members from the type, with no attribute. Unlike
[envelope payloads](#envelope-payloads), a union is encoded structurally inline, at compile time:
each value is written as its own manifest plus its fields. No runtime serializer lookup is involved.

| Property type | Members |
| --- | --- |
| An interface or abstract class | Every `[AkkaSerializable]` type that implements or derives from it, in this compilation or in a referenced assembly that references `Akka.Serialization.V2` |
| A [closed hierarchy](#closed-hierarchies) | Its `[AkkaSerializable]` cases |
| A [C# 15 union](#c-15-union-types) | The union's cases |
| The serializer's own protocol interface | The protocol's messages (see [A Protocol-Interface Field Is an Implicit Union](#a-protocol-interface-field-is-an-implicit-union)) |

```csharp
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
public sealed record OrderCancelled(
    [property: AkkaField(1)] string OrderId,
    [property: AkkaField(2)] string Reason) : IOrderEvent;

[AkkaSerializable(Manifest = "order-note-v1")]
public readonly record struct OrderNote(
    [property: AkkaField(1)] string OrderId,
    [property: AkkaField(2)] string Note) : IOrderEvent;
```

`OrderPlaced` shows a union member that is also a top-level protocol message. It has one manifest
and two roles. `OrderCancelled` is a member only: it never travels on its own, and its manifest
exists to tell the union's members apart. `OrderNote` shows a value-type member, dispatched
through boxing.

Some types are never unions:

* A sealed or concrete class is a [nested value](#nested-akkaserializable-value-objects), written
  as that class's own fields. Treating it as a union would change the field's wire format the
  moment someone added a subclass. A subclass value written there would lose its own fields, so
  when a class in this compilation derives from a concrete nested value class, the build fails
  (**AKKASG052**). Seal the class, or make the base abstract and remove its `[AkkaSerializable]`
  so the property becomes a union of its subclasses. The same check covers a concrete class used as
  a collection element, dictionary key, or dictionary value; there, sealing is the only fix.
* `object` is always an [envelope payload](#envelope-payloads).
* Framework types (the `System` namespace, such as `Stream` or `IComparable`, and Akka.NET's own
  core types, such as `ActorPath`) and collection
  interfaces report **AKKASG003**. So does an interface or abstract class used as a collection
  element, dictionary key, or dictionary value: only a property's own type can be a union.

To leave a type out of a union, don't implement the union's interface on it. A narrower interface,
implemented only by the members you want, gives a narrower union.

### Member Requirements

Every member type must meet three requirements. It must be **serializable**: an
`[AkkaSerializable]` class or struct handled by the same serializer (**AKKASG015**). It must be
**manifested**, since its manifest is the union's discriminator (**AKKASG016**). That manifest
must be unique within the union (**AKKASG017**). An abstract member is dead code, since dispatch
matches the exact runtime type and an abstract type is never a runtime type (**AKKASG036**,
warning). A member that is not sealed gets a warning (**AKKASG025**): it works, but a subtype the
generator didn't see fails at serialize time.

A concrete type in this compilation that implements or derives from a union's type but has no
`[AkkaSerializable]` would be left out of the dispatch and fail the first time it is written, so
it fails the build instead (**AKKASG048**), the union counterpart of **AKKASG029**. The check
covers union properties declared in this compilation; a union property on a message from a
referenced assembly is not checked yet. A union with no members at all fails with **AKKASG019**:
a union with no members can only throw.

### Exact-Runtime-Type Dispatch

Write dispatch matches the *exact* runtime type. It ignores the declared static type and any base
type. A value whose exact type is not a member fails with a `SerializationException`. This
includes a subtype of a member that the generator never saw, such as a type loaded at run time. The
generator never silently widens the value to the base type and loses state. On read, an
unrecognized manifest inside the union frame throws a `SerializationException` naming it. The
caller decides what to do with either failure. The generator never guesses.

### Hierarchies Across Assemblies

The members of an interface or abstract class can live on both sides of an assembly boundary. In a
layered codebase, the interface sits upstream of its implementations:

```csharp
// Core.dll
public interface IDestination
{
}

// Comms.dll, references Core.dll where IDestination lives.
[AkkaSerializable(Manifest = "dest-comms-v1")]
public sealed record CommsDestination([property: AkkaField(1)] string Host) : IDestination;

// Tech.dll, also references Core.dll.
[AkkaSerializable(Manifest = "dest-tech-v1")]
public sealed record TechDestination([property: AkkaField(1)] string Cluster) : IDestination;
```

The serializer's compilation sees every assembly it references, so a field typed `IDestination`
there is a union over both records. The message that declares the field can live in any of those
assemblies too: an `Envelope` with an `IDestination` property, declared in `Core.dll` next to the
interface, still gets both records when its serializer lives in an assembly that references
`Comms.dll` and `Tech.dll`. The serializer's assembly must reference every assembly that declares a
member; members in an assembly it cannot see are not part of the union. The wire format is the same either way: a union frame carries
each member's own manifest. Only a referenced assembly's public types are visible: an `internal`
implementor there is left out of the union and fails when it is first written, with no build error.

This ships with the same referenced-assembly walk Decision 19 uses for top-level dispatch; see
[Cross-Assembly Types](#cross-assembly-types) for the walk itself, and
[The One-Owner Rule](#the-one-owner-rule) for the same two costs and mitigations that apply here
too.

### Closed Hierarchies

A closed hierarchy fixes its derived types in its own declaration. The most portable form is an
abstract base whose constructors are all private, so only its nested types can derive from it:

```csharp
public abstract record Coverage
{
    private Coverage() { }

    [AkkaSerializable(Manifest = "paid-invoice")]
    public sealed record PaidInvoice([property: AkkaField(1)] long PublisherId, [property: AkkaField(2)] long InvoiceNumber) : Coverage;

    [AkkaSerializable(Manifest = "trial", AllowEmpty = true)]
    public sealed record Trial : Coverage;
}

[AkkaSerializable(Manifest = "period-covered")]
public sealed record PeriodCovered([property: AkkaField(1)] long SubscriptionId, [property: AkkaField(2)] Coverage Coverage) : ISubscriptionEvent;
```

This needs no particular compiler: `Coverage` is a union because it is abstract, and the private
constructor keeps anyone outside it from adding cases. The base must be abstract. A concrete class
is never a union, even when its constructors are all private: it can still hand out instances of
itself (a `static readonly` default, a factory method), and no union of its subclasses could write
one. Such a property keeps its **AKKASG007** error until you make the base abstract.

A C# 15 `closed record` (or `closed class`) works the same way, on `net10.0` as well as `net11.0`.
The generator recognizes the `closed` modifier, and also a type carrying
`System.Runtime.CompilerServices.IsClosedTypeAttribute`. On `net10.0` the compiler needs that
attribute (plus `UnionAttribute` and `IUnion` for unions) declared as an internal polyfill in your
project; Akka.NET does not ship them, since they would collide with the `net11.0` BCL.

### C# 15 Union Types

A property typed as a C# 15 `union` is a union field whose members are the union's cases. Each case
follows the same [member rules](#member-requirements) as any other union member (**AKKASG015**
through **AKKASG017**, **AKKASG025**, **AKKASG036**).

```csharp
[AkkaSerializable(Manifest = "paid-invoice")]
public sealed record PaidInvoice([property: AkkaField(1)] long PublisherId, [property: AkkaField(2)] long InvoiceNumber);

[AkkaSerializable(Manifest = "trial", AllowEmpty = true)]
public sealed record Trial;

public union Coverage(PaidInvoice, Trial);

[AkkaSerializable(Manifest = "period-covered")]
public sealed record PeriodCovered([property: AkkaField(1)] long SubscriptionId, [property: AkkaField(2)] Coverage Coverage) : ISubscriptionEvent;
```

Write dispatch matches the exact runtime type of the case the union holds. `default(Coverage)`, which
holds no case, is written as MessagePack nil and reads back as `default`. A `Coverage?` property works
too, but `null` and a `default(Coverage)` value both write nil, so both read back as `null`. A held
case is written exactly as the same type would be written in a class-hierarchy union field, so a held case
written with a closed record hierarchy reads back into a union of the same cases, and the other way
around. `default(Coverage)` has no hierarchy equivalent: its nil only reads back into a nullable field.

To send or persist the union itself, give the union an explicit manifest and make it part of the
serializer's protocol (or register it with `[AkkaSerializerIncludes<T>]`):

```csharp
[AkkaSerializable(Manifest = "coverage-v1")]
public union Coverage(PaidInvoice, Trial) : ISubscriptionEvent;
```

The top-level manifest identifies `Coverage`; its payload is the same case-manifest and case-body
frame used for a union field. Deserialization returns a boxed `Coverage`, so handlers for the union
type still match. Writing `default(Coverage)` as a top-level message throws a
`SerializationException`; the nil behavior for fields is unchanged. An attribute without a manifest
on the union reports **AKKASG051**. A standalone case retains its own distinct top-level manifest.

Case order does not affect the wire format. Adding a case requires readers to be upgraded before
writers use it; removing a case prevents reading stored values of that case. Switching between a
union and a closed hierarchy preserves the field frame, but changes the top-level manifest and
therefore needs a migration for top-level messages. Generic unions require a registration and
manifest for each closed construction using `[AkkaSerializerIncludes<T>]`.

A formatter registered for the union type with `[AkkaSerializerFormatter<TUnion, TFormatter>]` takes
precedence for fields. Not supported yet: union classes, and unions as collection elements or
dictionary values (`List<Coverage>` reports **AKKASG003**, as a collection of an interface does).

C# unions need the C# 15 compiler, which ships with the .NET 11 SDK. Akka.NET itself needs no
`net11.0` target: a project that targets `net10.0` declares `System.Runtime.CompilerServices.UnionAttribute`
and `System.Runtime.CompilerServices.IUnion` as internal types (Akka.NET does not ship them, since
they would collide with the `net11.0` BCL):

```csharp
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
    internal sealed class UnionAttribute : Attribute { }

    internal interface IUnion { object? Value { get; } }
}
```

## Including Messages Outside the Protocol

A serializer owns every `[AkkaSerializable]` type that implements its protocol interface. To add a
type that doesn't, include it on the serializer class with `[AkkaSerializerIncludes<T>]`:

```csharp
[AkkaSerializable(Manifest = "chat-response")]
public sealed record ChatResponse([property: AkkaField(0)] ChatResponseCode ResponseCode);

[AkkaSerializer<IWithUserId>("user-serializer", 120001)]
[AkkaSerializerIncludes<ChatResponse>]
public sealed partial class ChatSerializer : AkkaSerializer
{
    public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system);
}
```

The two attributes do different jobs. `[AkkaSerializable]` describes the type: its fields and its
manifest. `[AkkaSerializerIncludes<T>]` only says which serializer owns it. The included type
becomes a top-level message of the serializer, with its own dispatch arm, its own private helpers,
and its own concrete binding in the generated `CreateSerializerDetails(system)`. That binding is
what routes a value of that exact type to this serializer at runtime, since the type does not
implement the protocol the serializer is bound to.

Three rules apply, checked at build time:

* The included type must be `[AkkaSerializable]`, with its field schema readable from here,
  wherever it lives (including a referenced assembly).
* A non-generic type's manifest comes from its own `[AkkaSerializable(Manifest = ...)]`, the one
  place it is declared. Setting `Manifest` on `[AkkaSerializerIncludes<T>]` for a non-generic
  type fails the build (**AKKASG053**). A closed generic construction is the exception: it has no
  declaration of its own, so it takes its manifest from the include (see
  [Closed Generic Registrations](#closed-generic-registrations)).
* One type, one owner. A message type belongs to only one serializer's closed set in a compilation.
  See [The One-Owner Rule](#the-one-owner-rule).

## Closed Generic Registrations

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
[AkkaSerializerIncludes<Wrapper<OrderRequest>>(Manifest = "wrap-request-v1")]
[AkkaSerializerIncludes<Wrapper<OrderReceipt>>(Manifest = "wrap-receipt-v1")]
public sealed partial class ClosedGenericTestSerializer : AkkaSerializer
{
    public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system);
}
```

A closed construction has no declaration of its own to carry a manifest, so it takes one from its
include: `[AkkaSerializerIncludes<T>(Manifest = ...)]`. Each one then behaves exactly like any
[included type](#including-messages-outside-the-protocol). It gets its own manifest and dispatch arm. Its generic fields resolve against
the concrete type arguments. This mirrors `System.Text.Json`'s source generator, which requires
`[JsonSerializable]` on each closed construction instead of accepting an unbound generic.

A combined shape also works: a generic wrapper whose payload field is itself a closed union.
`EventWrapper<T>` declares no union of its own. At construction time, when `T` is `IOrderEvent`,
the substituted field type is an interface, so it is a union over `IOrderEvent`'s implementors. The
generator resolves and validates it against that type:

```csharp
[AkkaSerializable]
public sealed record EventWrapper<T>(
    [property: AkkaField(1)] string Id,
    [property: AkkaField(2)] T Body) : IClosedGenericTestProtocol;
```

Generated Write, Read, and `SizeOf` method names combine a readable type name with a stable suffix
from the full type identity, including generic arguments. Types can share a simple name across
namespaces or containing types. These private method names do not affect manifests or wire bytes.

Diagnostics guard this shape. **AKKASG022** fires when a generic definition implements the
protocol with no closed registrations at all. **AKKASG023** fires when a field uses an
unregistered closed construction. **AKKASG020** and **AKKASG021** guard the registration itself,
catching an invalid or a duplicate one. **AKKASG037**, info, fires when a `Manifest` is set on the
*open* definition, where the generator ignores it.

Anything the generator cannot see at build time does not exist. A construction assembled through
`Activator.CreateInstance` and `MakeGenericType`, over a type argument outside every registered or
expanded set, has no matching case. It fails at send time with an `ArgumentException` naming the
unsupported type. Reflection is not a path the generator accommodates.

### `ManifestPrefix`: Expanding a Registration over a Closed Set

A registration's type argument can itself have a **closed set**: the serializer's own protocol
interface, or any other type a property could use as a [union](#unions) (an interface, an abstract
class, or a closed hierarchy). Setting `ManifestPrefix` on such
a registration expands it to one closed construction per member of that set, instead of the single
construction named in the attribute:

```csharp
public interface ICommsMessage { }

[AkkaSerializable(Manifest = "dmac")]
public sealed record AcceptCassette([property: AkkaField(1)] int Layer) : ICommsMessage;

[AkkaSerializable(Manifest = "ocan")]
public sealed record OrderCancelled([property: AkkaField(1)] string OrderId) : ICommsMessage;

[AkkaSerializable]
public sealed record Envelope<T>(
    [property: AkkaField(1)] T Message,
    [property: AkkaField(2)] string TraceId);

[AkkaSerializer<ICommsMessage>("comms", 8677751)]
[AkkaSerializerIncludes<Envelope<ICommsMessage>>(ManifestPrefix = "env")]
public sealed partial class CommsSerializer : AkkaSerializer
{
    public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system);
}
```

This one attribute registers `Envelope<AcceptCassette>` and `Envelope<OrderCancelled>` -- one
construction per member of `ICommsMessage`'s closed set -- each with its own dispatch arm, private
helpers, and a manifest derived from the member's own. It does **not** register the literal
construction, `Envelope<ICommsMessage>` itself. Add `Manifest` alongside `ManifestPrefix` to also
register that literal construction:

```csharp
[AkkaSerializerIncludes<Envelope<ICommsMessage>>(ManifestPrefix = "env", Manifest = "env-any")]
```

The two properties combine into four cases:

| `Manifest` | `ManifestPrefix` | What gets registered |
|---|---|---|
| set | not set | Only the named construction. Today's behavior; no expansion. |
| not set | set | Only the expansion. The literal construction is not registered. |
| set | set | The literal construction under `Manifest`, plus the expansion under `ManifestPrefix`. |
| not set | not set | **AKKASG006** (a top-level message needs a manifest), unchanged from today. |

`ManifestPrefix` on a registration whose type argument is a concrete class, or has no closed set at
all, is **AKKASG040**: there is nothing to expand over.

### The Manifest Formula

Every member of a closed set already has its own manifest: **AKKASG006** requires one on a
top-level message, and **AKKASG016** requires one on a union member. The generator derives every
expanded construction's manifest from these, by a fixed formula, evaluated once, at build time. Both
sides of the wire produce the same literal strings; the runtime never builds or parses a manifest:

```text
manifest(explicit registration, Manifest = "m")   = "m"
manifest(M) for a member of the set               = the top-level manifest of M
manifest(G<M>, ManifestPrefix = "p")              = "p" + "/" + manifest(M)
manifest(G<M1, M2>, ManifestPrefix = "p")         = "p" + "/" + manifest(M1) + "/" + manifest(M2)
manifest(G<H<M>>, ManifestPrefix = "p")           = "p" + "/" + manifest(H<M>)
```

**Worked example 1: a single-argument expansion.** With the `CommsSerializer` example above,
`Envelope<AcceptCassette>` derives `"env" + "/" + "dmac"`, or `env/dmac`. `Envelope<OrderCancelled>`
derives `env/ocan`. Both are exact-inline constructions: the manifest already fixes `T`, so the
payload carries no discriminator of its own.

**Worked example 2: a nested construction.** Say `Envelope<AcceptCassette>` is *also* registered
explicitly, with its own manifest: `[AkkaSerializerIncludes<Envelope<AcceptCassette>>(Manifest =
"env-dmac-v2")]`. A second generic, `Pair<TFirst, TSecond>`, registers
`[AkkaSerializerIncludes<Pair<ICommsMessage, Envelope<AcceptCassette>>>(ManifestPrefix = "pair")]`. Its
first argument expands over the protocol set; its second argument, `Envelope<AcceptCassette>`, is a
*fixed* nested construction, not itself a closed set, so its manifest comes from that explicit
sibling registration: `manifest(Envelope<AcceptCassette>) = "env-dmac-v2"`. The derived manifest for
`Pair<AcceptCassette, Envelope<AcceptCassette>>` is therefore `"pair" + "/" + "dmac" + "/" +
"env-dmac-v2"`, or `pair/dmac/env-dmac-v2`. A nested construction that was never registered anywhere
has no manifest to compose, which is **AKKASG040** too -- the same shape as **AKKASG023** for an
unregistered nested field.

An explicit registration for one specific construction keeps its own manifest, overriding the
formula for that one member. This is how `Envelope<AcceptCassette>` above ended up with
`env-dmac-v2` instead of the plain `env/dmac` the formula alone would have derived. It is also the
only way to register a construction whose type argument is a concrete class, since a concrete class
has nothing to expand over.

Because every manifest is known at build time -- the explicit ones, each member's own, and every
derived one -- **AKKASG012** checks the whole set for collisions, including a case where a member's
own manifest contains the separator (for example prefix `env` with member `a/b`, against prefix
`env/a` with member `b`). The separator is a literal `/`, fixed for this rule.

The expansion rule applies to **every** type argument of a registration, not only the first. A
multi-argument generic expands to the product of its arguments' own sets. Because the number of
generated constructions can grow quickly, the generator reports it: **AKKASG042**, info, once per
expanding registration, naming the resulting count. There is no size threshold; every expansion is
reported.

### A Protocol-Interface Field Is an Implicit Union

A field whose static type is *exactly* the serializer's own protocol interface is a union over the
protocol set, like any other [interface field](#unions). This is what makes the literal construction
above encodable at all: `Envelope<ICommsMessage>`'s `Message` field is typed `ICommsMessage`, and
without this rule it would be **AKKASG003**. The closed set is the same one `ManifestPrefix`
expands over, and the wire frame is the same union frame any other union uses -- a discriminator
manifest, then the member's own fields.

As for any union, only the field's *own* static type counts: a collection of the protocol interface
(`List<ICommsMessage>`) still reports **AKKASG003**. Unmarked implementors of the protocol report
**AKKASG029** at the serializer, not **AKKASG048** at each field.

### The One-Owner Rule

A message type belongs to only one serializer's closed set in one compilation, whether it gets
there by implementing a protocol or by an explicit `[AkkaSerializerIncludes<T>]` adoption. Two serializers
claiming the same type -- two adoptions, or one adoption colliding with the other serializer's own
protocol membership -- is **AKKASG041**, reported at both serializers' own declarations. Across
compilations that cannot see each other, a build-time check cannot run at all: neither compilation
sees the other's serializer. Applications must coordinate those bindings explicitly --
see [Cross-Assembly Ownership](#cross-assembly-ownership).

The protocol set `ManifestPrefix` expands over spans referenced assemblies too, the same set
[top-level dispatch](#cross-assembly-types) uses: every `[AkkaSerializable]` implementor this
compilation can see, local or referenced.

## Cross-Assembly Types

A top-level protocol implementor, a nested field, a union member, or a closed generic's own open
definition can all live in a **referenced assembly** instead of the
serializer's own compilation.

**Top-level protocol implementors.** The generator walks every referenced assembly that itself
references `Akka.Serialization.V2`, from that assembly's compiled metadata, and adopts every
`[AkkaSerializable]` implementor of the protocol interface it finds. No attribute opts an assembly
in. No runtime type scanning happens; the walk runs once, at build time, inside the generator.

```csharp
// Contracts.dll
public interface IOrderEvent { }

// Messages.dll (references Contracts.dll)
[AkkaSerializable(Manifest = "placed-v1")] public sealed record OrderPlaced(...) : IOrderEvent;
[AkkaSerializable(Manifest = "cancel-v1")] public sealed record OrderCancelled(...) : IOrderEvent;

// Wire.dll (references Contracts.dll and Messages.dll). The serializer lives here.
[AkkaSerializer<IOrderEvent>("orders", 4001)]
public sealed partial class OrderSerializer : AkkaSerializer
{
    public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system);
}
// OrderPlaced and OrderCancelled both get a Manifest dispatch arm, a typeof() binding, and their
// own Write/Read/SizeOf helpers, even though neither is declared in Wire.dll.
```

The same walk backs [union fields](#unions) typed as an interface or abstract class, including
**A Protocol-Interface Field Is an Implicit Union**: each closed set spans this compilation plus
every referenced assembly that references V2 (see
[Hierarchies Across Assemblies](#hierarchies-across-assemblies)).

**Two shapes that predate this walk keep working the same way:**

* **A nested field type.** `Pay` in assembly B can have an `[AkkaField]` property typed `Money`,
  where `Money` is declared and `[AkkaSerializable]` in assembly A.
* **A generic definition behind a closed generic registration.** `Wrapper<T>` can live in assembly
  A. `[AkkaSerializerIncludes<Wrapper<int>>]` on the serializer in assembly B still registers it. This
  shape needed no new work: a closed generic registration already reads its target off the type
  symbol directly, which works the same way regardless of which assembly declared it.

The generator reads a referenced type's schema from that assembly's **compiled metadata**: the
`[AkkaSerializable]` attribute, the `[AkkaField]` properties and their indexes, and the constructor
shape. It runs that schema through the same extraction path a local type uses, so the generated
`Write`, `Read`, and `SizeOf` helpers are identical to what they would be if the type were declared
locally. The referenced assembly needs no reference to the source generator and gets no generated
code of its own. Nothing couples the two assemblies at run time.

**The accessibility rule.** A referenced type must be `[AkkaSerializable]`, and it must be
accessible from the serializer's own assembly: public, or internal with `[InternalsVisibleTo]`
granted there. The same rule applies to each of its own `[AkkaField]` properties. A type that is not
`[AkkaSerializable]` anywhere this generator can see fails with **AKKASG007** (nested field) or
**AKKASG015** (union member), the same diagnostics a same-assembly gap already uses. A type that
carries the attribute but that the generator's own assembly cannot fully read fails instead with
**AKKASG039**: the type itself might be internal with no `InternalsVisibleTo` grant, or one of its
`[AkkaField]` properties might be. The problem can also sit one level down, inside the referenced
type's own nested field or union member; the message names the type and member that is actually
inaccessible, wherever it sits. A top-level implementor the walk cannot see or read simply does not
join the closed set; there is no local reference site to report an accessibility failure against,
so it stays silently absent, the same way an unrelated type would.

Every diagnostic in this section reports at the **local reference site**: the property that names
the type, the `[AkkaSerializerIncludes<T>]` attribute application, or (for a placement
mistake) the local type declaration -- see [Serializer Placement](#serializer-placement). The
referenced assembly never gets a diagnostic, because no generator work runs there. Each message
offers two fixes: register a hand-written `[AkkaSerializerFormatter<TTarget, TFormatter>]`, or
declare the type directly alongside the serializer.

Two serializers in different assemblies can both nest, or both adopt, the same referenced type.
Each one generates its own private copy of the `Write`/`Read`/`SizeOf` helpers, built from the same
metadata. The copies are byte-identical by construction; there is no shared runtime codec to keep
in sync. Two serializers adopting the SAME top-level implementor is a different problem -- see
[The One-Owner Rule](#the-one-owner-rule) and [Serializer Placement](#serializer-placement).

## Serializer Placement

A serializer's identity (its name, id, and protocol) need not live in the same assembly as its
messages. What cannot move is this: **the code that encodes a message must be generated in a
compilation that can see that message** -- the message's own assembly, or one below it. A
compilation never sees an assembly that depends on it, so a serializer declared upstream of its own
messages can never dispatch them. Three rules turn that mistake into a compile-time diagnostic
instead of a silent gap; a fourth improves the runtime message for the case neither rule catches.

**AKKASG043, error, "protocol is owned by an upstream serializer".** A type declared in this
compilation implements a protocol that an upstream serializer -- one declared in a referenced
assembly -- already binds. That upstream assembly can never see this one, so the type would never
be dispatched. Reported at the type's own declaration, since that is the only local site there is.
Fix it by moving the type upstream, or by moving the serializer to an assembly that references this
one.

**AKKASG044, warning, "serializer has no messages".** A serializer has no messages in its own
compilation, none in any referenced assembly, and no registrations of its own. This is very likely
a placement mistake: the messages may live in an assembly that depends on this one, which this
serializer can never see. A message assembly whose serializer lives in a host below it is a
legitimate shape, though, so this stays a warning, not an error.

**AKKASG031, error, extended across assemblies.** Two serializers binding the same protocol already
collide when both are declared in one compilation. The same check now also fires when a LOCAL
serializer's protocol is bound by a serializer declared in a referenced assembly: the runtime
binding lookup can only route a value to one serializer. Reported at the local serializer's own
attribute, naming the upstream one.

**The runtime exception, improved.** A value the generated code cannot match now names both
assemblies: the value's own runtime assembly (`obj.GetType().Assembly`) and the assembly the
serializer was generated in. Naming both makes a placement mistake that slipped past every
build-time check diagnosable from the exception alone.

### Cross-Assembly Ownership

The generator detects conflicting ownership when the serializers are visible to the same
compilation. Serializers in assemblies that cannot see each other need explicitly coordinated
bindings; core `SerializationSetup` does not enforce the generator's ownership rule.

## Envelope Payloads

A property typed `object` or `object?` is always an **Akka serializer boundary**, not a
structurally encoded value. The static type alone carries this meaning; no attribute is involved.
The generator does not generate inline MessagePack code for such a field. Instead, it emits a
runtime lookup. It asks the actor system's `Serialization` extension for whatever serializer is
bound to the payload's *actual* runtime type. It stores the result as a
`{serializer id, manifest, bytes}` triple.

An earlier `dev` build marked this with an explicit `[AkkaEnvelopePayload]` attribute. That
attribute was removed before the first 1.6 beta; the `object` type now carries the same meaning on
its own.

The same rule applies to a collection element. A `List<object>`, an `object[]`, an
`ImmutableList<object>`, or a `Dictionary<string, object>` value typed `object` or `object?` is an
envelope payload for each element, with the same runtime lookup. A `null` element round-trips as
`null` when the element type is `object?`. A dictionary *key* typed `object` is not supported
(**AKKASG003**), since a round-tripped payload has no stable identity to hash.

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
    [property: AkkaField(1)] object Inner) : IEnvelopeBenchmarkProtocol;

[AkkaSerializable(Manifest = "benchmark-inner-envelope-v1")]
public sealed record BenchmarkInnerEnvelope(
    [property: AkkaField(0)] string EnvelopeId,
    [property: AkkaField(1)] object Payload) : IEnvelopeBenchmarkProtocol;
```

(`BenchmarkOuterEnvelope` and `BenchmarkInnerEnvelope` are trimmed fixtures from
`src/benchmark/Akka.Benchmarks/Serialization/GeneratedMessagePackSerializerBenchmarks.cs`.)

`Inner` is `object`, so the consumer casts it back to the concrete type it expects:

```csharp
var inner = (BenchmarkInnerEnvelope)outer.Inner;
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

## Hand-Written Formatters

Some types cannot be annotated with `[AkkaSerializable]`. A core Akka type like
`Akka.Actor.Address` is the most common case. It lives in an assembly that cannot reference
`Akka.Serialization.V2` without creating a dependency cycle. Apply
`[AkkaSerializerFormatter<TTarget, TFormatter>]` to the `[AkkaSerializer]` class instead. It routes
every field of type `TTarget`, and every collection element, dictionary key, and dictionary value of
type `TTarget`, through a hand-written `IAkkaMessagePackFormatter<TTarget>` implementation:

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
    public static partial global::Akka.Serialization.SerializerDetails CreateSerializerDetails(global::Akka.Actor.ExtendedActorSystem system);
}
```

### Formatters Inside Collections

A registered formatter applies at every position a `TTarget` can sit, in every collection shape the
generator supports: `T[]`, `List<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`,
`ImmutableArray<T>`, `ImmutableList<T>`, `ImmutableHashSet<T>`, the three dictionary shapes (as key
or as value), and nested combinations such as `Dictionary<string, List<Address>>`. With
`[AkkaSerializerFormatter<Address, AddressFormatter>]` registered, these all work:

```csharp
[AkkaSerializable(Manifest = "route-v1")]
public sealed record Route(
    [property: AkkaField(1)] List<Address> Hops,
    [property: AkkaField(2)] Dictionary<Address, long> LastSeen,
    [property: AkkaField(3)] ImmutableArray<Address> Candidates) : IRoutingProtocol;
```

The wire form is the array or map the collection already uses, with each element exactly the bytes the
formatter writes at field position. There is no wrapper, so `Hops` above is `array(n)` of the 4-element
`Address` arrays. As at field position, the generator owns `nil`: a `null` element, or an absent
`Nullable<T>` element, is written as `nil` and the formatter only sees present values. `SizeOf` is
summed over the elements, so the exact-size contract holds for the whole collection.

A formatter registered for a type the generator also supports natively takes precedence at every
position, not only at field position. Register an `IActorRef` formatter, for example, and `List<IActorRef>`
uses it. Without one, `IActorRef` keeps its native encoding.

### The Two Built-in Formatters

`Akka.Serialization.V2` ships two formatters. They let `Address` and `ActorPath` fields work out
of the box. `AddressFormatter` writes `Address` as a 4-element array:
`[Protocol, System, Host-or-nil, Port-or-nil]`. This is byte-identical to the wire format
`ArteryControlMessageSerializer`'s hand-rolled `WriteAddress` already produces. So a generated
serializer that registers it interoperates with that format today. `ActorPathFormatter` writes a
single transport-aware string. It follows the same convention the generator applies to native
`IActorRef` fields. It uses the thread-static transport context when one is set. Otherwise it uses
the owning system's default address, when constructed with an `ExtendedActorSystem`. Otherwise it
uses the path's own address.

## Wire Format

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

## Versioning and Rolling Upgrades

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

## Diagnostics Reference

Every id below is a `DiagnosticDescriptor` in `AkkaSerializerGenerator.cs`. Three ids are
intentionally absent. **AKKASG030** does not exist. The C# compiler itself already rejects
duplicate `[AkkaSerializer<T>]` attributes on one class, as `CS0579`: "Duplicate attribute". So the
generator never needs its own diagnostic for that case. **AKKASG034** does not exist either. It
used to fire when a registered closed generic construction implemented no protocol and was
unreachable from any field. An include now always makes its type a top-level message (see
[Including Messages Outside the Protocol](#including-messages-outside-the-protocol)), so that condition can no longer
occur, and the id stays a permanent gap. **AKKASG035** does not exist either. It used to fire when a
field carried both `[AkkaEnvelopePayload]` and a union attribute. That attribute was retired, so the
diagnostic was retired with it, and the id stays a permanent gap. **AKKASG018**, **AKKASG038**,
**AKKASG049**, and **AKKASG050** do not exist either. They checked hand-written `[AkkaUnion]` member
lists. Before the first 1.6 beta, the generator started working out a union's members from the
property's type, the attribute was removed, and those ids stay permanent gaps.

| Id | Severity | Title | Meaning |
|---|---|---|---|
| AKKASG001 | Error | Serializer name must be a non-empty string | The `Name` argument to `[AkkaSerializer<T>]` is null, empty, or whitespace. |
| AKKASG002 | Error | Serializer id must be a positive integer | The `SerializerId` argument is zero or negative. |
| AKKASG003 | Error | Unsupported field type | An `[AkkaField]` property's type isn't one the generator can encode, or it is a collection with an element, key, or value that neither the generator nor a registered formatter can encode. On a framework interface or abstract class, a collection element, or a type parameter, the message adds a hint: only a property's own interface or abstract class type is a [union](#unions); type the property as `object` for an envelope payload. |
| AKKASG004 | Error | No serializable fields | An `[AkkaSerializable]` type has no `[AkkaField]` properties and didn't opt in with `AllowEmpty`. Reports at the type declaration even without a serializer. |
| AKKASG005 | Error | Duplicate field index | Two `[AkkaField]` properties on the same type share an index. |
| AKKASG006 | Error | Top-level message manifest is required | A type implementing the serializer's protocol has no `Manifest`. |
| AKKASG007 | Error | Nested value object serialization definition is required | A field's type isn't `[AkkaSerializable]` with its own `[AkkaField]`s and has no registered formatter. For a type declared in another assembly (including the BCL), the message names the assembly and offers a formatter, or `[AkkaSerializable]` in the type's own assembly. |
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
| AKKASG019 | Error | Union member set is invalid | The union has no members: no `[AkkaSerializable]` type implements or derives from the property's interface or abstract class. Mark its implementations, or type the property as `object`. |
| AKKASG020 | Error | Closed generic registration is invalid | An `[AkkaSerializerIncludes<T>]` registration's target isn't a closed type (generic or not) whose definition is `[AkkaSerializable]`. |
| AKKASG021 | Error | Duplicate closed generic registration | The same closed construction is registered more than once on one serializer. |
| AKKASG022 | Error | Generic serializable type requires closed generic registrations | A generic `[AkkaSerializable]` type implements the protocol but has no closed registrations. |
| AKKASG023 | Error | Closed generic field type is not registered | A field uses a closed generic construction that was never registered with `[AkkaSerializerIncludes<T>]`. |
| AKKASG024 | Error | Generated member name collision | Two distinct type identities produced the same generated member name despite their stable suffixes. Report the generator collision with both declarations. |
| AKKASG025 | Warning | Union member type is not sealed | An undeclared subtype of this (concrete, non-sealed) member will fail serialization if it's ever sent. |
| AKKASG026 | Error | No matching constructor | No constructor covers every required `[AkkaField]` property, so the type can't be reconstructed. |
| AKKASG027 | Warning | Constructor parameter not covered by [AkkaField] | A defaulted constructor parameter has no matching `[AkkaField]` property and silently resets on every deserialize. |
| AKKASG028 | Error | [AkkaField] must be on an accessible instance property | The property is static, or otherwise unreachable from the generated code. |
| AKKASG029 | Error | Protocol message type is not [AkkaSerializable] | A type implements the serializer's protocol but isn't `[AkkaSerializable]`, so it's invisible to the generated dispatch. |
| AKKASG031 | Error | Protocol interface bound by multiple serializers | Two `[AkkaSerializer]` classes bind the same protocol interface. Extended across assemblies: also fires when a local serializer's protocol is bound by a serializer declared in a referenced assembly. See [Serializer Placement](#serializer-placement). |
| AKKASG032 | Error | Serializer class shape is invalid | The `[AkkaSerializer]` class isn't `partial`, is generic, or doesn't derive from `AkkaSerializer`. |
| AKKASG033 | Error | Protocol type must be an interface | The `TProtocol` type argument to `[AkkaSerializer<TProtocol>]` isn't an interface. |
| AKKASG036 | Warning | Union member type is abstract | An abstract union member can never be the exact runtime type, so its dispatch branch is dead code. |
| AKKASG037 | Info | Manifest on a generic [AkkaSerializable] definition is ignored | A `Manifest` set on the *open* generic definition is ignored; only closed constructions carry one. |
| AKKASG039 | Error | Referenced type or member is not accessible | A referenced-assembly nested field or union member type is `[AkkaSerializable]`, but this compilation cannot see it, or one of its own `[AkkaField]` properties, or a type it itself nests. See [Cross-assembly types](#cross-assembly-types). |
| AKKASG040 | Error | ManifestPrefix has no closed set to expand | A registration sets `ManifestPrefix`, but its type argument isn't generic, is a concrete class, or otherwise has no closed member set (or a fixed nested argument was never itself registered). See [ManifestPrefix](#manifestprefix-expanding-a-registration-over-a-closed-set). |
| AKKASG041 | Error | Message type is owned by more than one serializer | A type belongs to more than one serializer's closed set in this compilation, by protocol membership or adoption. See [The One-Owner Rule](#the-one-owner-rule). |
| AKKASG042 | Info | Closed-set expansion produced constructions | A `ManifestPrefix` registration expanded; the message names how many constructions it produced. |
| AKKASG043 | Error | Protocol is owned by an upstream serializer | A type declared here implements a protocol that a serializer in a referenced assembly already binds; that assembly can never see this type. See [Serializer Placement](#serializer-placement). |
| AKKASG044 | Warning | Serializer has no messages | A serializer has no messages here, in any referenced assembly, or in its own registrations -- often a sign the serializer is placed upstream of its messages. See [Serializer Placement](#serializer-placement). |
| AKKASG045 | Error | Message type is not accessible to generated code | An `[AkkaSerializable]` type, or a type that contains it, is `private`, `protected`, or `private protected`. The generated serializer cannot name it. Make it `internal` or `public`. |
| AKKASG046 | Warning | Stored property is not serialized | A public declared auto-property or positional record property has no `[AkkaField]`. Add a field index, or suppress this diagnostic if omission is intentional. Computed properties, static members, inherited members, and fields are outside this check. |
| AKKASG047 | Warning | Field annotations on an unmarked type are ignored | A type declares `[AkkaField]` properties without `[AkkaSerializable]`. Add the type marker, including for nested payloads. If a custom formatter handles the type, remove the unused annotations or suppress the warning. |
| AKKASG048 | Error | Union implementor is not [AkkaSerializable] | A concrete type in this compilation derives from or implements a union property's type but isn't `[AkkaSerializable]`, so the union leaves it out and it fails when first serialized. See [Member Requirements](#member-requirements). |
| AKKASG051 | Error | A C# union requires a manifest | Set an explicit `Manifest` on `[AkkaSerializable]` when marking a C# 15 union declaration. |
| AKKASG052 | Error | Nested value type has subclasses | A property writes a concrete `[AkkaSerializable]` class that is not sealed as a nested value (as its own type, or as a collection element, key, or value), and a class in this compilation derives from it. A nested value is written as the base class's own fields, so a subclass value would lose its own. Seal the class; for the property's own type you can instead make it abstract and remove its `[AkkaSerializable]` so the property becomes a [union](#unions). |
| AKKASG053 | Error | Manifest belongs on the included type | `[AkkaSerializerIncludes<T>]` sets `Manifest`, but `T` is not generic. A non-generic type declares its manifest on its own `[AkkaSerializable]`; remove `Manifest` from the include and declare it there. See [Including Messages Outside the Protocol](#including-messages-outside-the-protocol). |

## Limitations Today and Planned Changes

The generator reads what Roslyn can see at compile time: syntax trees for this compilation's own
declarations, and compiled metadata for a referenced assembly's. Two concrete consequences follow
today. First, a construction built through reflection over a type the generator never saw at
compile time fails only when it is first sent. This never happens at compile time. A generic
instantiation assembled dynamically at runtime is one example. Second, a dictionary whose key type is
`object` fails compilation with **AKKASG003**. A round-tripped envelope payload has no stable
identity to use as a lookup key.

A top-level protocol implementor, a nested field type, a union member type, and a closed generic's
own open definition can all live in a referenced assembly today;
see [Cross-Assembly Types](#cross-assembly-types) and [Serializer Placement](#serializer-placement).

The following work is planned. None of it ships on `dev` today. It is tracked against
[issue #8384](https://github.com/akkadotnet/akka.net/issues/8384) and
`openspec/changes/messagepack-sourcegen-validation/design.md`:

* **Serializer parts.** Splitting a serializer's identity from its encoders, so a serializer
  declared upstream of its own messages can still dispatch them, composed explicitly at startup with
  no scanning. A documented follow-up, not needed by the shapes this generator targets today.
