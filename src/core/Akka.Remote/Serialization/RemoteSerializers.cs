//-----------------------------------------------------------------------
// <copyright file="RemoteSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Collections.Immutable;
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
    /// INTERNAL API. Akka.Remote's serializers, bindings and ids. They register as defaults when the module is deployed; Remote.conf carries no rows for them.
    /// </summary>
    internal sealed class RemoteSerializers : ModuleSerializers
    {
        private static readonly Config PrimitiveDefaults = ConfigurationFactory.ParseString("use-legacy-behavior = on");

        public override ImmutableHashSet<SerializerDetails> Create(ExtendedActorSystem system) => ImmutableHashSet.Create(
            SerializerDetails.Create("akka-containers", new MessageContainerSerializer(system),
                ImmutableHashSet.Create(typeof(ActorSelectionMessage))),
            SerializerDetails.Create("akka-misc", new MiscMessageSerializer(system),
                ImmutableHashSet.Create(
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
                    typeof(RemoteRouterConfig))),
            // the only built-in serializer with its own settings block; without Remote.conf, use the same default Remote.conf sets
            SerializerDetails.Create("primitive",
                new PrimitiveSerializers(system,
                    system.Settings.Config.GetConfig("akka.actor.serialization-settings.primitive") ?? PrimitiveDefaults),
                ImmutableHashSet.Create(typeof(string), typeof(int), typeof(long))),
            SerializerDetails.Create("proto", new ProtobufSerializer(system),
                ImmutableHashSet.Create(typeof(Google.Protobuf.IMessage))),
            SerializerDetails.Create("daemon-create", new DaemonMsgCreateSerializer(system),
                ImmutableHashSet.Create(typeof(DaemonMsgCreate))),
            SerializerDetails.Create("akka-system-msg", new SystemMessageSerializer(system),
                ImmutableHashSet.Create(typeof(SystemMessage))),
            SerializerDetails.Create("artery-control", new ArteryControlMessageSerializer(system),
                ImmutableHashSet.Create(typeof(IArteryControlMessage)))
        );
    }
}
