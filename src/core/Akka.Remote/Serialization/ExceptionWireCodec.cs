// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using Akka.Actor;
using Akka.Serialization;
using Akka.Serialization.V2;

namespace Akka.Remote.Serialization;

internal interface IExceptionWire
{
}

// A genuine generated fragment codec, never registered as a serializer row. Its required
// positive id reuses the existing Misc id; no id, alias or write binding is added globally.
[AkkaSerializer<IExceptionWire>("akka-exception-fragment", 16)]
internal sealed partial class ExceptionWireCodec : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}

[AkkaSerializable(Manifest = "exception-fragment-v1")]
internal sealed record ExceptionWire(
    [property: AkkaField(1)] int Descriptor,
    [property: AkkaField(2)] string? Message,
    [property: AkkaField(3)] string? StackTrace,
    [property: AkkaField(4)] string? Source,
    [property: AkkaField(5)] ExceptionWire? Inner,
    [property: AkkaField(6)] ExceptionFieldWire[] Fields,
    [property: AkkaField(7)] string? JitTypeName,
    [property: AkkaField(8)] string? ClassName,
    [property: AkkaField(9)] int HResult,
    [property: AkkaField(10)] string? HelpUrl,
    [property: AkkaField(11)] string? RemoteStackTrace,
    [property: AkkaField(12)] int RemoteStackIndex,
    [property: AkkaField(13)] string? ExceptionMethod,
    [property: AkkaField(14)] ExceptionDataEntryWire[] Data,
    [property: AkkaField(15)] string? JitForwardedFrom) : IExceptionWire;

[AkkaSerializable]
internal sealed record ExceptionFieldWire([property: AkkaField(1)] string Name,
    [property: AkkaField(2)] int DeclaredType, [property: AkkaField(3)] ExceptionValueWire Value,
    [property: AkkaField(4)] string? JitDeclaredType);

[AkkaSerializable]
internal sealed record ExceptionDataEntryWire([property: AkkaField(1)] ExceptionValueWire Key,
    [property: AkkaField(2)] ExceptionValueWire Value);

[AkkaSerializable]
internal sealed record ExceptionValueWire([property: AkkaField(1)] int Kind,
    [property: AkkaField(2)] object? Payload, [property: AkkaField(3)] ExceptionWire? Exception,
    [property: AkkaField(4)] IActorRef? ActorRef, [property: AkkaField(5)] long? Ticks);
