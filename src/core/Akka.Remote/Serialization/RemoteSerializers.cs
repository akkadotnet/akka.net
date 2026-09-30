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
        // the constructor reflection picks for Remote.conf; each class has one constructor, so it always gets that one
        public override IReadOnlyList<BuiltInSerializer> Serializers { get; } = new[]
        {
            new BuiltInSerializer("akka-containers", typeof(MessageContainerSerializer), (system, _) => new MessageContainerSerializer(system),
                new[] { typeof(ActorSelectionMessage) }),
            new BuiltInSerializer("akka-misc", typeof(MiscMessageSerializer), (system, _) => new MiscMessageSerializer(system),
                new[]
                {
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
                    typeof(RemoteRouterConfig),
                }),
            new BuiltInSerializer("primitive", typeof(PrimitiveSerializers), (system, config) => new PrimitiveSerializers(system, config),
                new[] { typeof(string), typeof(int), typeof(long) }),
            new BuiltInSerializer("proto", typeof(ProtobufSerializer), (system, _) => new ProtobufSerializer(system),
                new[] { typeof(Google.Protobuf.IMessage) }),
            new BuiltInSerializer("daemon-create", typeof(DaemonMsgCreateSerializer), (system, _) => new DaemonMsgCreateSerializer(system),
                new[] { typeof(DaemonMsgCreate) }),
            new BuiltInSerializer("akka-system-msg", typeof(SystemMessageSerializer), (system, _) => new SystemMessageSerializer(system),
                new[] { typeof(SystemMessage) }),
            new BuiltInSerializer("artery-control", typeof(ArteryControlMessageSerializer), (system, _) => new ArteryControlMessageSerializer(system),
                new[] { typeof(IArteryControlMessage) }),
        };
    }
}
