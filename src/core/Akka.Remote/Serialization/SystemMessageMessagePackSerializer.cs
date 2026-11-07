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
        var output = new ArrayBufferWriter<byte>();
        Serialize(obj, output);
        return output.WrittenSpan.ToArray();
    }

    public override int Serialize(object obj, IBufferWriter<byte> writer)
    {
        ISystemMessageWire wire = obj switch
        {
            Create value => new CreateWire(value.Failure),
            Recreate value => new RecreateWire(value.Cause),
            Suspend => new SuspendWire(),
            Resume value => new ResumeWire(value.CausedByFailure),
            Terminate => new TerminateWire(),
            Supervise value => new SuperviseWire(Path(value.Child), value.Async),
            Watch value when value.GetType() == typeof(Watch) => new WatchWire(Path(value.Watchee), Path(value.Watcher)),
            Unwatch value => new UnwatchWire(Path(value.Watchee), Path(value.Watcher)),
            Failed value => new FailedWire(Path(value.Child), value.Cause, value.Uid),
            DeathWatchNotification value => new DeathWatchNotificationWire(Path(value.Actor),
                value.ExistenceConfirmed, value.AddressTerminated),
            _ => throw new ArgumentException($"Unsupported system message [{obj?.GetType()}].", nameof(obj))
        };
        var previous = Akka.Serialization.Serialization.CurrentTransportInformation;
        try
        {
            if (previous is null)
                Akka.Serialization.Serialization.CurrentTransportInformation = system.Provider.SerializationInformation;
            // Path-only controls have already materialized all fields and can write directly.
            if (wire is not (CreateWire or RecreateWire or ResumeWire or FailedWire))
                return _codec.Serialize(wire, writer);
            // A late exception/payload formatter failure must not leave a partial system message
            // in the transport's caller-owned writer.
            var staged = new ArrayBufferWriter<byte>();
            _codec.Serialize(wire, staged);
            staged.WrittenSpan.CopyTo(writer.GetSpan(staged.WrittenCount));
            writer.Advance(staged.WrittenCount);
            return staged.WrittenCount;
        }
        finally
        {
            Akka.Serialization.Serialization.CurrentTransportInformation = previous;
        }
    }

    public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
    {
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
