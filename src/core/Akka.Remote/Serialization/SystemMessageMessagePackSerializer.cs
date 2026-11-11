// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Serialization;
using Akka.Serialization.V2;

namespace Akka.Remote.Serialization;

/// <summary>Generated system-message protocol. Legacy id 22 remains registered for reads.</summary>
internal sealed class SystemMessageMessagePackSerializer : SerializerV2
{
    internal const int SerializerIdentifier = 62;
    private const int MaximumCachedBufferCapacity = 1024;
    [ThreadStatic] private static ArrayBufferWriter<byte>? _arrayBuffer;
    private readonly SystemMessageMessagePackCodec _codec;

    public SystemMessageMessagePackSerializer(ExtendedActorSystem system) : base(system)
    {
        _codec = new SystemMessageMessagePackCodec(system);
    }

    public override int Identifier => SerializerIdentifier;

    // Watch is publicly extensible; only its exact built-in type has a protocol manifest.
    public override string Manifest(object obj) => obj switch
    {
        Create => SystemMessageManifests.Create,
        Recreate => SystemMessageManifests.Recreate,
        Suspend => SystemMessageManifests.Suspend,
        Resume => SystemMessageManifests.Resume,
        Terminate => SystemMessageManifests.Terminate,
        Supervise => SystemMessageManifests.Supervise,
        Watch value when value.GetType() == typeof(Watch) => SystemMessageManifests.Watch,
        Unwatch => SystemMessageManifests.Unwatch,
        Failed => SystemMessageManifests.Failed,
        DeathWatchNotification => SystemMessageManifests.DeathWatchNotification,
        _ => throw new ArgumentException($"Unsupported system message [{obj?.GetType()}].", nameof(obj))
    };

    public override byte[] ToBinary(object obj)
    {
        if (obj is Suspend or Terminate)
            return [0x80]; // The generated AllowEmpty record is an empty MessagePack map.

        var wire = ToWire(obj);
        if (HasExceptionPayload(wire))
        {
            using var output = new PooledPayloadWriter();
            SerializeWire(wire, output);
            return output.WrittenSpan.ToArray();
        }

        // Small path-only controls need UTF-8 reservation headroom, but renting and
        // returning a pooled array on each call can dominate their encoding cost.
        // Lease this bounded scratch writer so reentrant calls cannot share it.
        var scratch = _arrayBuffer ?? new ArrayBufferWriter<byte>();
        _arrayBuffer = null;
        try
        {
            SerializeWire(wire, scratch);
            return scratch.WrittenSpan.ToArray();
        }
        finally
        {
            if (scratch.Capacity <= MaximumCachedBufferCapacity)
            {
                scratch.Clear();
                _arrayBuffer = scratch;
            }
        }
    }

    public override int Serialize(object obj, IBufferWriter<byte> writer)
    {
        if (obj is Suspend or Terminate)
        {
            writer.GetSpan(1)[0] = 0x80;
            writer.Advance(1);
            return 1;
        }

        var wire = ToWire(obj);
        if (!HasExceptionPayload(wire))
            return SerializeWire(wire, writer);

        // A late exception/payload formatter failure must not leave a partial system
        // message in the transport's caller-owned writer.
        using var staged = new PooledPayloadWriter();
        SerializeWire(wire, staged);
        staged.WrittenSpan.CopyTo(writer.GetSpan(staged.WrittenCount));
        writer.Advance(staged.WrittenCount);
        return staged.WrittenCount;
    }

    private static bool HasExceptionPayload(ISystemMessageWire wire) => wire is
        CreateWire { Cause: not null } or RecreateWire { Cause: not null }
        or ResumeWire { Cause: not null } or FailedWire { Cause: not null };

    private int SerializeWire(ISystemMessageWire wire, IBufferWriter<byte> writer)
    {
        var previous = Akka.Serialization.Serialization.CurrentTransportInformation;
        try
        {
            if (previous is null)
                Akka.Serialization.Serialization.CurrentTransportInformation = system.Provider.SerializationInformation;
            return _codec.Serialize(wire, writer);
        }
        finally
        {
            Akka.Serialization.Serialization.CurrentTransportInformation = previous;
        }
    }

    public override object FromBinary(byte[] bytes, string manifest)
    {
        if (bytes is { Length: 1 } && bytes[0] == 0x80 && ReadControl(manifest) is { } control)
            return control;
        return Deserialize(new ReadOnlySequence<byte>(bytes), manifest);
    }

    public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
    {
        // Avoid an intermediate wire record for the canonical fieldless controls. All
        // other inputs still use the generated reader, including future fields and errors.
        if (bytes.IsSingleSegment && bytes.FirstSpan.Length == 1 && bytes.FirstSpan[0] == 0x80
            && ReadControl(manifest) is { } control)
            return control;

        var wire = _codec.Deserialize(bytes, manifest);
        return wire switch
        {
            CreateWire value when value.Cause is null or ActorInitializationException =>
                new Create((ActorInitializationException?)value.Cause),
            RecreateWire value => new Recreate(value.Cause),
            SuspendWire => new Suspend(),
            ResumeWire value => new Resume(value.Cause),
            TerminateWire => new Terminate(),
            SuperviseWire value => new Supervise(Resolve(value.Child), value.Async),
            WatchWire value => new Watch((IInternalActorRef)Resolve(value.Watchee)!, (IInternalActorRef)Resolve(value.Watcher)!),
            UnwatchWire value => new Unwatch((IInternalActorRef)Resolve(value.Watchee)!, (IInternalActorRef)Resolve(value.Watcher)!),
            FailedWire value => new Failed(Resolve(value.Child), value.Cause, value.Uid),
            // Legacy DeathWatchNotification resolves an empty path through the provider; the
            // other system messages explicitly map an empty path to null.
            DeathWatchNotificationWire value => new DeathWatchNotification(system.Provider.ResolveActorRef(value.Actor),
                value.ExistenceConfirmed, value.AddressTerminated),
            _ => throw new SerializationException($"Unsupported system-message payload for manifest [{manifest}].")
        };
    }

    private static SystemMessage? ReadControl(string manifest) => manifest switch
    {
        SystemMessageManifests.Suspend => new Suspend(),
        SystemMessageManifests.Terminate => new Terminate(),
        _ => null
    };

    private static ISystemMessageWire ToWire(object obj) => obj switch
    {
        Create value => new CreateWire(value.Failure),
        Recreate value => new RecreateWire(value.Cause),
        Resume value => new ResumeWire(value.CausedByFailure),
        Supervise value => new SuperviseWire(Path(value.Child), value.Async),
        Watch value when value.GetType() == typeof(Watch) => new WatchWire(Path(value.Watchee), Path(value.Watcher)),
        Unwatch value => new UnwatchWire(Path(value.Watchee), Path(value.Watcher)),
        Failed value => new FailedWire(Path(value.Child), value.Cause, value.Uid),
        DeathWatchNotification value => new DeathWatchNotificationWire(Path(value.Actor),
            value.ExistenceConfirmed, value.AddressTerminated),
        _ => throw new ArgumentException($"Unsupported system message [{obj?.GetType()}].", nameof(obj))
    };

    private static string Path(IActorRef? actor) => Akka.Serialization.Serialization.SerializedActorPath(actor);
    private IActorRef? Resolve(string path) => string.IsNullOrEmpty(path) ? null : system.Provider.ResolveActorRef(path);
}

internal static class SystemMessageManifests
{
    internal const string Create = "Akka.Dispatch.SysMsg.Create, Akka";
    internal const string Recreate = "Akka.Dispatch.SysMsg.Recreate, Akka";
    internal const string Suspend = "Akka.Dispatch.SysMsg.Suspend, Akka";
    internal const string Resume = "Akka.Dispatch.SysMsg.Resume, Akka";
    internal const string Terminate = "Akka.Dispatch.SysMsg.Terminate, Akka";
    internal const string Supervise = "Akka.Dispatch.SysMsg.Supervise, Akka";
    internal const string Watch = "Akka.Dispatch.SysMsg.Watch, Akka";
    internal const string Unwatch = "Akka.Dispatch.SysMsg.Unwatch, Akka";
    internal const string Failed = "Akka.Dispatch.SysMsg.Failed, Akka";
    internal const string DeathWatchNotification = "Akka.Dispatch.SysMsg.DeathWatchNotification, Akka";
}

internal interface ISystemMessageWire { }

[AkkaSerializer<ISystemMessageWire>("akka-system-msg-codec", SystemMessageMessagePackSerializer.SerializerIdentifier)]
[AkkaSerializerFormatter<Exception, ExceptionMessagePackFormatter>]
internal sealed partial class SystemMessageMessagePackCodec : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}

[AkkaSerializable(Manifest = SystemMessageManifests.Create)]
internal sealed record CreateWire([property: AkkaField(1)] Exception? Cause) : ISystemMessageWire;
[AkkaSerializable(Manifest = SystemMessageManifests.Recreate)]
internal sealed record RecreateWire([property: AkkaField(1)] Exception? Cause) : ISystemMessageWire;
[AkkaSerializable(Manifest = SystemMessageManifests.Suspend, AllowEmpty = true)]
internal sealed record SuspendWire : ISystemMessageWire;
[AkkaSerializable(Manifest = SystemMessageManifests.Resume)]
internal sealed record ResumeWire([property: AkkaField(1)] Exception? Cause) : ISystemMessageWire;
[AkkaSerializable(Manifest = SystemMessageManifests.Terminate, AllowEmpty = true)]
internal sealed record TerminateWire : ISystemMessageWire;
[AkkaSerializable(Manifest = SystemMessageManifests.Supervise)]
internal sealed record SuperviseWire([property: AkkaField(1)] string Child, [property: AkkaField(2)] bool Async) : ISystemMessageWire;
[AkkaSerializable(Manifest = SystemMessageManifests.Watch)]
internal sealed record WatchWire([property: AkkaField(1)] string Watchee, [property: AkkaField(2)] string Watcher) : ISystemMessageWire;
[AkkaSerializable(Manifest = SystemMessageManifests.Unwatch)]
internal sealed record UnwatchWire([property: AkkaField(1)] string Watchee, [property: AkkaField(2)] string Watcher) : ISystemMessageWire;
[AkkaSerializable(Manifest = SystemMessageManifests.Failed)]
internal sealed record FailedWire([property: AkkaField(1)] string Child, [property: AkkaField(2)] Exception? Cause,
    [property: AkkaField(3)] long Uid) : ISystemMessageWire;
[AkkaSerializable(Manifest = SystemMessageManifests.DeathWatchNotification)]
internal sealed record DeathWatchNotificationWire([property: AkkaField(1)] string Actor,
    [property: AkkaField(2)] bool ExistenceConfirmed, [property: AkkaField(3)] bool AddressTerminated) : ISystemMessageWire;
