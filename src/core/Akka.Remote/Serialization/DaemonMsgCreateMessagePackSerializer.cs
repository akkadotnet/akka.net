//-----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;
using Akka.Serialization;
using Akka.Serialization.V2;
using Akka.Util;

namespace Akka.Remote.Serialization;

/// <summary>Generated remote-deployment envelope. Actor construction still requires dynamic type loading.</summary>
internal sealed class DaemonMsgCreateMessagePackSerializer : SerializerV2
{
    private readonly DaemonMsgCreateMessagePackCodec _codec;

    public DaemonMsgCreateMessagePackSerializer(ExtendedActorSystem system) : base(system)
    {
        _codec = new DaemonMsgCreateMessagePackCodec(system);
    }

    public override int Identifier => 43;

    public override string Manifest(object obj)
    {
        if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
            throw UnsupportedDeployment();
        return obj is DaemonMsgCreate ? "a" : throw UnknownMessage(obj);
    }

    public override int Serialize(object obj, IBufferWriter<byte> writer)
    {
        if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
            throw UnsupportedDeployment();
        return _codec.Serialize(ToWire(obj), writer);
    }

    public override int SizeHint(object obj)
    {
        if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
            throw UnsupportedDeployment();
        return _codec.SizeHint(ToWire(obj));
    }

    public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
    {
        if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
            throw UnsupportedDeployment();
        if (manifest != "a")
            throw new SerializationException($"Unknown remote-deployment manifest [{manifest}] for serializer [{Identifier}].");
        return FromWire((DaemonCreateWire)_codec.Deserialize(bytes, manifest));
    }

    private static DaemonCreateWire ToWire(object obj)
    {
        if (obj is not DaemonMsgCreate message)
            throw UnknownMessage(obj);
        return new DaemonCreateWire(new DaemonPropsWire(message.Props.Type.TypeQualifiedName(),
                ToWire(message.Props.Deploy), message.Props.Arguments), ToWire(message.Deploy), message.Path,
            Akka.Serialization.Serialization.SerializedActorPath(message.Supervisor));
    }

    private static DaemonDeployWire ToWire(Deploy deploy) => new(deploy.Path, deploy.Config,
        deploy.RouterConfig.Equals(NoRouter.Instance) ? null : deploy.RouterConfig,
        deploy.Scope == Deploy.NoScopeGiven ? null : deploy.Scope, deploy.Dispatcher, deploy.StashCapacity);

    [RequiresUnreferencedCode("Remote deployment resolves an actor type from its wire name and constructs its Props.")]
    private DaemonMsgCreate FromWire(DaemonCreateWire wire)
    {
        var actorClass = Type.GetType(wire.Props.ActorClass) ?? throw new SerializationException(
            $"Could not resolve actor type [{wire.Props.ActorClass}] during DaemonMsgCreate deserialization.");
        return new DaemonMsgCreate(new Props(FromWire(wire.Props.Deploy), actorClass, wire.Props.Arguments),
            FromWire(wire.Deploy), wire.Path, system.Provider.ResolveActorRef(wire.SupervisorPath));
    }

    private static Deploy FromWire(DaemonDeployWire wire)
    {
        // A null config used NullSerializer id 0 in the protobuf envelope, whose reader
        // reconstructed Config.Empty rather than passing null into Deploy.
        var deploy = new Deploy(wire.Path, (Config?)wire.Config ?? Config.Empty, (RouterConfig?)wire.RouterConfig ?? NoRouter.Instance,
            (Scope?)wire.Scope ?? Deploy.NoScopeGiven,
            string.IsNullOrEmpty(wire.Dispatcher) ? Deploy.NoDispatcherGiven : wire.Dispatcher);
        // Match the legacy protobuf reader, including its pre-v1.5.4 zero-stash compatibility rule.
        return wire.StashCapacity > 0 ? deploy.WithStashCapacity(wire.StashCapacity) : deploy;
    }

    private static SerializationException UnsupportedDeployment() => new(
        "Remote deployment (DaemonMsgCreate) requires actor type resolution and construction. " +
        "It is unsupported with Akka.DynamicTypeLoading disabled; no native actor factory is registered.");

    private static ArgumentException UnknownMessage(object obj) => new(
        $"Cannot serialize [{obj.GetType()}] using [{nameof(DaemonMsgCreateMessagePackSerializer)}].", nameof(obj));
}

internal interface IDaemonCreateWire;

[AkkaSerializer<IDaemonCreateWire>("daemon-create-v2-codec", 43)]
internal sealed partial class DaemonMsgCreateMessagePackCodec : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}

// Legacy id 3 has no manifest. New id 43 uses the nonempty native token "a".
[AkkaSerializable(Manifest = "a")]
internal sealed record DaemonCreateWire([property: AkkaField(1)] DaemonPropsWire Props,
    [property: AkkaField(2)] DaemonDeployWire Deploy, [property: AkkaField(3)] string Path,
    [property: AkkaField(4)] string SupervisorPath) : IDaemonCreateWire;

[AkkaSerializable]
internal sealed record DaemonPropsWire([property: AkkaField(1)] string ActorClass,
    [property: AkkaField(2)] DaemonDeployWire Deploy, [property: AkkaField(3)] object?[] Arguments);

[AkkaSerializable]
internal sealed record DaemonDeployWire([property: AkkaField(1)] string Path,
    [property: AkkaField(2)] object? Config, [property: AkkaField(3)] object? RouterConfig,
    [property: AkkaField(4)] object? Scope, [property: AkkaField(5)] string Dispatcher,
    [property: AkkaField(6)] int StashCapacity);
