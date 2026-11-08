// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Remote.Routing;
using Akka.Routing;
using Akka.Serialization;
using Akka.Serialization.V2;

namespace Akka.Remote.Serialization;

/// <summary>Generated miscellaneous protocol; legacy id 16 and its bytes remain available.</summary>
internal sealed class MiscMessageMessagePackSerializer : SerializerV2
{
    internal const int SerializerIdentifier = 56;
    private readonly MiscMessageMessagePackCodec _codec;
    public MiscMessageMessagePackSerializer(ExtendedActorSystem system) : base(system)
    {
        _codec = new MiscMessageMessagePackCodec(system);
    }
    public override int Identifier => SerializerIdentifier;
    public override string Manifest(object obj) => obj switch
    {
        Identify => "ID",
        ActorIdentity => "AID",
        IActorRef => "AR",
        PoisonPill => "PP",
        Kill => "K",
        IntentionalRestart => "IR",
        RemoteWatcher.Heartbeat => "RWHB",
        RemoteWatcher.HeartbeatRsp => "RWHR",
        LocalScope => "LS",
        RemoteScope => "RS",
        Config => "CF",
        FromConfig => "FC",
        DefaultResizer => "DR",
        RoundRobinPool => "RORRP",
        BroadcastPool => "ROBP",
        RandomPool => "RORP",
        ScatterGatherFirstCompletedPool => "ROSGP",
        TailChoppingPool => "ROTCP",
        ConsistentHashingPool => "ROCHP",
        RemoteRouterConfig => "RORRC",
        Status.Failure => "STF",
        Status.Success => "STS",
        _ => throw new ArgumentException($"Unsupported miscellaneous message [{obj?.GetType()}].", nameof(obj))
    };

    public override byte[] ToBinary(object obj)
    {
        var output = new ArrayBufferWriter<byte>();
        Serialize(obj, output);
        return output.WrittenSpan.ToArray();
    }

    public override int Serialize(object obj, IBufferWriter<byte> writer)
    {
        IMiscMessageWire wire = obj switch
        {
            Identify v => new IdentifyWire(v.MessageId),
            ActorIdentity v => new ActorIdentityWire(v.MessageId, Path(v.Subject)),
            IActorRef v => new ActorRefWire(v is Nobody ? "nobody" : Path(v)),
            PoisonPill => new PoisonPillWire(),
            Kill => new KillWire(),
            IntentionalRestart => new IntentionalRestartWire(),
            RemoteWatcher.Heartbeat => new HeartbeatWire(),
            RemoteWatcher.HeartbeatRsp v => new HeartbeatResponseWire(v.AddressUid),
            LocalScope => new LocalScopeWire(),
            RemoteScope v => new RemoteScopeWire(ToWire(v.Address)),
            Config v => new ConfigWire(v.IsEmpty ? "" : v.Root.ToString()),
            FromConfig v => new FromConfigWire(v.Resizer, Dispatcher(v.RouterDispatcher),
                v == FromConfig.Instance || (v.Resizer is null && string.IsNullOrEmpty(v.RouterDispatcher))),
            DefaultResizer v => new DefaultResizerWire(v.LowerBound, v.UpperBound, v.PressureThreshold,
                v.RampupRate, v.BackoffThreshold, v.BackoffRate, v.MessagesPerResize),
            RoundRobinPool v => new RoundRobinWire(ToWire(v)),
            BroadcastPool v => new BroadcastWire(ToWire(v)),
            RandomPool v => new RandomWire(ToWire(v)),
            ScatterGatherFirstCompletedPool v => new ScatterGatherWire(ToWire(v), v.Within),
            TailChoppingPool v => new TailChoppingWire(ToWire(v), v.Within, v.Interval),
            ConsistentHashingPool v => new ConsistentHashingWire(ToWire(v)),
            RemoteRouterConfig v => new RemoteRouterWire(v.Local, v.Nodes.Select(ToWire).ToArray()),
            Status.Failure v => new FailureWire(v.Cause, v.State),
            Status.Success v => new SuccessWire(v.Status),
            _ => throw new ArgumentException($"Unsupported miscellaneous message [{obj?.GetType()}].", nameof(obj))
        };
        var previous = Akka.Serialization.Serialization.CurrentTransportInformation;
        try
        {
            if (previous is null)
                Akka.Serialization.Serialization.CurrentTransportInformation = system.Provider.SerializationInformation;
            if (wire is not (IdentifyWire or ActorIdentityWire or FromConfigWire or RoundRobinWire or BroadcastWire
                or RandomWire or ScatterGatherWire or TailChoppingWire or ConsistentHashingWire or RemoteRouterWire
                or FailureWire or SuccessWire))
                return _codec.Serialize(wire, writer);
            // Each envelope/exception can fail late (including registered user formatters).
            // Keep caller transport output intact until the whole message has encoded.
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

    public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => _codec.Deserialize(bytes, manifest) switch
    {
        IdentifyWire v => new Identify(v.MessageId),
        ActorIdentityWire v => new ActorIdentity(v.MessageId, Resolve(v.Subject)),
        ActorRefWire v => v.Path == "nobody" ? ActorRefs.Nobody : system.Provider.ResolveActorRef(v.Path),
        PoisonPillWire => PoisonPill.Instance,
        KillWire => Kill.Instance,
        IntentionalRestartWire => IntentionalRestart.Instance,
        HeartbeatWire => RemoteWatcher.Heartbeat.Instance,
        HeartbeatResponseWire v => new RemoteWatcher.HeartbeatRsp(v.Uid),
        LocalScopeWire => LocalScope.Instance,
        RemoteScopeWire v => new RemoteScope(FromWire(v.Address)),
        ConfigWire v => v.Content.Length == 0 ? Config.Empty : ConfigurationFactory.ParseString(v.Content),
        FromConfigWire v => v.IsLegacyEmpty ? FromConfig.Instance
            : new FromConfig((Resizer?)v.Resizer, Pool.DefaultSupervisorStrategy, Dispatcher(v.Dispatcher)),
        DefaultResizerWire v => new DefaultResizer(v.LowerBound, v.UpperBound, v.PressureThreshold,
            v.RampupRate, v.BackoffThreshold, v.BackoffRate, v.MessagesPerResize),
        RoundRobinWire v => new RoundRobinPool(v.Pool.Instances, (Resizer?)v.Pool.Resizer, Pool.DefaultSupervisorStrategy, Dispatcher(v.Pool.Dispatcher), v.Pool.UsePoolDispatcher),
        BroadcastWire v => new BroadcastPool(v.Pool.Instances, (Resizer?)v.Pool.Resizer, Pool.DefaultSupervisorStrategy, Dispatcher(v.Pool.Dispatcher), v.Pool.UsePoolDispatcher),
        RandomWire v => new RandomPool(v.Pool.Instances, (Resizer?)v.Pool.Resizer, Pool.DefaultSupervisorStrategy, Dispatcher(v.Pool.Dispatcher), v.Pool.UsePoolDispatcher),
        ScatterGatherWire v => new ScatterGatherFirstCompletedPool(v.Pool.Instances, (Resizer?)v.Pool.Resizer, v.Within, Pool.DefaultSupervisorStrategy, Dispatcher(v.Pool.Dispatcher), v.Pool.UsePoolDispatcher),
        TailChoppingWire v => new TailChoppingPool(v.Pool.Instances, (Resizer?)v.Pool.Resizer, Pool.DefaultSupervisorStrategy, Dispatcher(v.Pool.Dispatcher), v.Within, v.Interval, v.Pool.UsePoolDispatcher),
        ConsistentHashingWire v => new ConsistentHashingPool(v.Pool.Instances, (Resizer?)v.Pool.Resizer, Pool.DefaultSupervisorStrategy, Dispatcher(v.Pool.Dispatcher), v.Pool.UsePoolDispatcher),
        RemoteRouterWire v => new RemoteRouterConfig((Pool)v.Local, v.Nodes.Select(FromWire)),
        FailureWire v => new Status.Failure(v.Cause, v.State),
        SuccessWire v => new Status.Success(v.State),
        _ => throw new SerializationException($"Unsupported miscellaneous payload for manifest [{manifest}].")
    };

    private static string Dispatcher(string? value) => string.IsNullOrEmpty(value) ? Dispatchers.DefaultDispatcherId : value;
    private static string Path(IActorRef? actor) => Akka.Serialization.Serialization.SerializedActorPath(actor);
    private IActorRef? Resolve(string path) => string.IsNullOrEmpty(path) ? null : system.Provider.ResolveActorRef(path);
    private static GenericPoolWire ToWire(Pool pool) => new(pool.NrOfInstances, pool.Resizer, Dispatcher(pool.RouterDispatcher), pool.UsePoolDispatcher);
    private static MiscAddressWire ToWire(Address address) => new(address.Protocol, address.System,
        address.Host ?? throw new ArgumentException("Legacy miscellaneous remote addresses require a hostname.", nameof(address)), address.Port);
    private static Address FromWire(MiscAddressWire value) => new(value.Protocol, value.System, value.Host, value.Port == 0 ? null : value.Port);
}

internal interface IMiscMessageWire { }

[AkkaSerializer<IMiscMessageWire>("akka-misc-codec", MiscMessageMessagePackSerializer.SerializerIdentifier)]
[AkkaSerializerFormatter<Exception, ExceptionMessagePackFormatter>]
internal sealed partial class MiscMessageMessagePackCodec : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}

[AkkaSerializable(Manifest = "ID")]
internal sealed record IdentifyWire([property: AkkaField(1)] object? MessageId) : IMiscMessageWire;

[AkkaSerializable(Manifest = "AID")]
internal sealed record ActorIdentityWire([property: AkkaField(1)] object? MessageId, [property: AkkaField(2)] string Subject) : IMiscMessageWire;

[AkkaSerializable(Manifest = "AR")]
internal sealed record ActorRefWire([property: AkkaField(1)] string Path) : IMiscMessageWire;

[AkkaSerializable(Manifest = "PP", AllowEmpty = true)]
internal sealed record PoisonPillWire : IMiscMessageWire;

[AkkaSerializable(Manifest = "K", AllowEmpty = true)]
internal sealed record KillWire : IMiscMessageWire;

[AkkaSerializable(Manifest = "IR", AllowEmpty = true)]
internal sealed record IntentionalRestartWire : IMiscMessageWire;

[AkkaSerializable(Manifest = "RWHB", AllowEmpty = true)]
internal sealed record HeartbeatWire : IMiscMessageWire;

[AkkaSerializable(Manifest = "RWHR")]
internal sealed record HeartbeatResponseWire([property: AkkaField(1)] long Uid) : IMiscMessageWire;

[AkkaSerializable(Manifest = "LS", AllowEmpty = true)]
internal sealed record LocalScopeWire : IMiscMessageWire;

[AkkaSerializable(Manifest = "RS")]
internal sealed record RemoteScopeWire([property: AkkaField(1)] MiscAddressWire Address) : IMiscMessageWire;

[AkkaSerializable(Manifest = "CF")]
internal sealed record ConfigWire([property: AkkaField(1)] string Content) : IMiscMessageWire;

[AkkaSerializable(Manifest = "FC")]
internal sealed record FromConfigWire([property: AkkaField(1)] object? Resizer, [property: AkkaField(2)] string Dispatcher, [property: AkkaField(3)] bool IsLegacyEmpty) : IMiscMessageWire;

[AkkaSerializable(Manifest = "DR")]
internal sealed record DefaultResizerWire([property: AkkaField(1)] int LowerBound, [property: AkkaField(2)] int UpperBound,
    [property: AkkaField(3)] int PressureThreshold, [property: AkkaField(4)] double RampupRate,
    [property: AkkaField(5)] double BackoffThreshold, [property: AkkaField(6)] double BackoffRate, [property: AkkaField(7)] int MessagesPerResize) : IMiscMessageWire;

[AkkaSerializable(Manifest = "RORRC")]
internal sealed record RemoteRouterWire([property: AkkaField(1)] object Local, [property: AkkaField(2)] MiscAddressWire[] Nodes) : IMiscMessageWire;

[AkkaSerializable(Manifest = "STF")]
internal sealed record FailureWire([property: AkkaField(1)] Exception? Cause, [property: AkkaField(2)] object? State) : IMiscMessageWire;

[AkkaSerializable(Manifest = "STS")]
internal sealed record SuccessWire([property: AkkaField(1)] object? State) : IMiscMessageWire;

[AkkaSerializable(Manifest = "RORRP")]
internal sealed record RoundRobinWire([property: AkkaField(1)] GenericPoolWire Pool) : IMiscMessageWire;

[AkkaSerializable(Manifest = "ROBP")]
internal sealed record BroadcastWire([property: AkkaField(1)] GenericPoolWire Pool) : IMiscMessageWire;

[AkkaSerializable(Manifest = "RORP")]
internal sealed record RandomWire([property: AkkaField(1)] GenericPoolWire Pool) : IMiscMessageWire;

[AkkaSerializable(Manifest = "ROSGP")]
internal sealed record ScatterGatherWire([property: AkkaField(1)] GenericPoolWire Pool, [property: AkkaField(2)] TimeSpan Within) : IMiscMessageWire;

[AkkaSerializable(Manifest = "ROTCP")]
internal sealed record TailChoppingWire([property: AkkaField(1)] GenericPoolWire Pool, [property: AkkaField(2)] TimeSpan Within, [property: AkkaField(3)] TimeSpan Interval) : IMiscMessageWire;

[AkkaSerializable(Manifest = "ROCHP")]
internal sealed record ConsistentHashingWire([property: AkkaField(1)] GenericPoolWire Pool) : IMiscMessageWire;

[AkkaSerializable]
internal sealed record GenericPoolWire([property: AkkaField(1)] int Instances, [property: AkkaField(2)] object? Resizer,
    [property: AkkaField(3)] string Dispatcher, [property: AkkaField(4)] bool UsePoolDispatcher);

// Remote scopes and router nodes carry complete addresses, unlike DData's inferred-system fragment.
[AkkaSerializable]
internal sealed record MiscAddressWire([property: AkkaField(1)] string Protocol, [property: AkkaField(2)] string System,
    [property: AkkaField(3)] string Host, [property: AkkaField(4)] int? Port);
