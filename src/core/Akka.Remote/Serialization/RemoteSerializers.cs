//-----------------------------------------------------------------------
// <copyright file="RemoteSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.Remote.Artery;
using Akka.Remote.Routing;
using Akka.Routing;
using Akka.Serialization;

namespace Akka.Remote.Serialization
{
    /// <summary>
    /// INTERNAL API. The serializer and binding rows of Remote.conf, so they resolve without reflection.
    /// </summary>
    internal sealed class RemoteSerializers : ModuleSerializers
    {
        // each class has one constructor, so it is the one reflection picks; Remote.conf gives primitive its settings
        public override IReadOnlyList<ModuleSerializer> Serializers { get; } = new[]
        {
            new ModuleSerializer(typeof(MessageContainerSerializer), (system, _) => new MessageContainerSerializer(system)),
            new ModuleSerializer(typeof(MiscMessageSerializer), (system, _) => new MiscMessageSerializer(system)),
            new ModuleSerializer(typeof(PrimitiveSerializers), (system, config) => new PrimitiveSerializers(system, config)),
            new ModuleSerializer(typeof(ProtobufSerializer), (system, _) => new ProtobufSerializer(system)),
            new ModuleSerializer(typeof(DaemonMsgCreateSerializer), (system, _) => new DaemonMsgCreateSerializer(system)),
            new ModuleSerializer(typeof(SystemMessageSerializer), (system, _) => new SystemMessageSerializer(system)),
            new ModuleSerializer(typeof(ArteryControlMessageSerializer), (system, _) => new ArteryControlMessageSerializer(system)),
        };

        public override IReadOnlyList<Type> BoundTypes { get; } = new[]
        {
            typeof(ActorSelectionMessage),
            typeof(DaemonMsgCreate),
            typeof(Google.Protobuf.IMessage),
            typeof(Identify),
            typeof(ActorIdentity),
            typeof(IActorRef),
            typeof(PoisonPill),
            typeof(IntentionalRestart),
            typeof(Kill),
            typeof(Status.Failure),
            typeof(Status.Success),
            typeof(RemoteScope),
            typeof(FromConfig),
            typeof(DefaultResizer),
            typeof(RoundRobinPool),
            typeof(BroadcastPool),
            typeof(RandomPool),
            typeof(ScatterGatherFirstCompletedPool),
            typeof(TailChoppingPool),
            typeof(ConsistentHashingPool),
            typeof(Config),
            typeof(RemoteWatcher.Heartbeat),
            typeof(RemoteWatcher.HeartbeatRsp),
            typeof(IArteryControlMessage),
            typeof(RemoteRouterConfig),
            typeof(SystemMessage),
            typeof(string),
            typeof(int),
            typeof(long),
        };
    }
}
