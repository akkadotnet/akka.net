//-----------------------------------------------------------------------
// <copyright file="MiscMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Remote.Routing;
using Akka.Routing;
using Akka.Serialization;
using Akka.Util;
using Akka.Util.Internal;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Akka.Remote.Serialization
{
    public class MiscMessageSerializer : SerializerWithStringManifest
    {
        private const string IdentifyManifest = "A";
        private const string ActorIdentityManifest = "AI";
        private const string PoisonPillManifest = "P";
        private const string KillManifest = "K";
        private const string RemoteWatcherHearthbeatManifest = "RWHB";
        private const string RemoteWatcherHearthbeatRspManifest = "RWHR";
        private const string LocalScopeManifest = "LS";
        private const string RemoteScopeManifest = "RS";
        private const string ConfigManifest = "CF";
        private const string FromConfigManifest = "FC";
        private const string DefaultResizerManifest = "DR";
        private const string RoundRobinPoolManifest = "RORRP";
        private const string BroadcastPoolManifest = "ROBP";
        private const string RandomPoolManifest = "RORP";
        private const string ScatterGatherPoolManifest = "ROSGP";
        private const string TailChoppingPoolManifest = "ROTCP";
        private const string RemoteRouterConfigManifest = "RORRC";

        private static readonly byte[] EmptyBytes = new byte[0];

        public MiscMessageSerializer(ExtendedActorSystem system) : base(system)
        {

        }

        public override byte[] ToBinary(object obj)
        {
            if (obj is Identify) return IdentifyToProto((Identify)obj);
            if (obj is ActorIdentity) return ActorIdentityToProto((ActorIdentity)obj);
            if (obj is PoisonPill) return EmptyBytes;
            if (obj is Kill) return EmptyBytes;
            if (obj is RemoteWatcher.Heartbeat) return EmptyBytes;
            if (obj is RemoteWatcher.HeartbeatRsp) return HeartbeatRspToProto((RemoteWatcher.HeartbeatRsp)obj);
            if (obj is LocalScope) return EmptyBytes;
            if (obj is RemoteScope) return RemoteScopeToProto((RemoteScope)obj);
            if (obj is DefaultResizer) return DefaultResizerToProto((DefaultResizer)obj);
            if (obj is RoundRobinPool) return RoundRobinPoolToProto((RoundRobinPool)obj);
            if (obj is BroadcastPool) return BroadcastPoolToProto((BroadcastPool)obj);
            if (obj is RandomPool) return RandomPoolToProto((RandomPool)obj);
            if (obj is ScatterGatherFirstCompletedPool) return ScatterGatherFirstCompletedPoolToProto((ScatterGatherFirstCompletedPool)obj);
            if (obj is TailChoppingPool) return TailChoppingPoolToProto((TailChoppingPool)obj);
            if (obj is RemoteRouterConfig) return RemoteRouterConfigToProto((RemoteRouterConfig)obj);

            throw new ArgumentException($"Cannot serialize object of type [{obj.GetType().TypeQualifiedName()}]");
        }

        public override string Manifest(object obj)
        {
            if (obj is Identify) return IdentifyManifest;
            if (obj is ActorIdentity) return ActorIdentityManifest;
            if (obj is PoisonPill) return PoisonPillManifest;
            if (obj is Kill) return KillManifest;
            if (obj is RemoteWatcher.Heartbeat) return RemoteWatcherHearthbeatManifest;
            if (obj is RemoteWatcher.HeartbeatRsp) return RemoteWatcherHearthbeatRspManifest;
            if (obj is LocalScope) return LocalScopeManifest;
            if (obj is RemoteScope) return RemoteScopeManifest;
            if (obj is DefaultResizer) return DefaultResizerManifest;
            if (obj is RoundRobinPool) return RoundRobinPoolManifest;
            if (obj is BroadcastPool) return BroadcastPoolManifest;
            if (obj is RandomPool) return RandomPoolManifest;
            if (obj is ScatterGatherFirstCompletedPool) return ScatterGatherPoolManifest;
            if (obj is TailChoppingPool) return TailChoppingPoolManifest;
            if (obj is RemoteRouterConfig) return RemoteRouterConfigManifest;

            throw new ArgumentException($"Cannot deserialize object of type [{obj.GetType().TypeQualifiedName()}]");
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            if (manifest == IdentifyManifest) return IdentifyFromProto(bytes);
            if (manifest == ActorIdentityManifest) return ActorIdentityFromProto(bytes);
            if (manifest == PoisonPillManifest) return PoisonPill.Instance;
            if (manifest == KillManifest) return Kill.Instance;
            if (manifest == RemoteWatcherHearthbeatManifest) return RemoteWatcher.Heartbeat.Instance;
            if (manifest == RemoteWatcherHearthbeatRspManifest) return HearthbeatRspFromProto(bytes);
            if (manifest == LocalScopeManifest) return LocalScope.Instance;
            if (manifest == RemoteScopeManifest) return RemoteScopeFromProto(bytes);
            if (manifest == DefaultResizerManifest) return DefaultResizerFromProto(bytes);
            if (manifest == RoundRobinPoolManifest) return RoundRobinPoolFromProto(bytes);
            if (manifest == BroadcastPoolManifest) return BroadcastPoolFromProto(bytes);
            if (manifest == RandomPoolManifest) return RandomPoolFromProto(bytes);
            if (manifest == ScatterGatherPoolManifest) return ScatterGatherFirstCompletedPoolFromProto(bytes);
            if (manifest == TailChoppingPoolManifest) return TailChoppingPoolFromProto(bytes);
            if (manifest == RemoteRouterConfigManifest) return RemoteRouterConfigFromProto(bytes);
 
            throw new ArgumentException($"Unimplemented deserialization of message with manifest [{manifest}] in [${nameof(MiscMessageSerializer)}]");
        }

        //
        // Identify
        //
        private byte[] IdentifyToProto(Identify identify)
        {
            var protoIdentify = new Proto.Msg.Identify();
            if (identify.MessageId != null)
                protoIdentify.MessageId = PayloadToProto(identify.MessageId);
            return protoIdentify.ToByteArray();
        }

        private Identify IdentifyFromProto(byte[] bytes)
        {
            var protoMessage = Proto.Msg.Identify.Parser.ParseFrom(bytes);
            if (protoMessage.MessageId == null)
                return new Identify(null);
            return new Identify(PayloadFrom(protoMessage.MessageId));
        }

        //
        // ActorIdentity
        //
        private byte[] ActorIdentityToProto(ActorIdentity actorIdentity)
        {
            var protoIdentify = new Proto.Msg.ActorIdentity();
            protoIdentify.CorrelationId = PayloadToProto(actorIdentity.MessageId);
            protoIdentify.Path = Akka.Serialization.Serialization.SerializedActorPath(actorIdentity.Subject);
            return protoIdentify.ToByteArray();
        }

        private ActorIdentity ActorIdentityFromProto(byte[] bytes)
        {
            var protoMessage = Proto.Msg.ActorIdentity.Parser.ParseFrom(bytes);
            return new ActorIdentity(PayloadFrom(protoMessage.CorrelationId), ResolveActorRef(protoMessage.Path));
        }

        //
        // RemoteWatcher.HeartbeatRsp
        //
        private byte[] HeartbeatRspToProto(RemoteWatcher.HeartbeatRsp heartbeatRsp)
        {
            var message = new Proto.Msg.RemoteWatcherHeartbeatResponse();
            message.Uid = (ulong)heartbeatRsp.AddressUid; // TODO: change to uint32
            return message.ToByteArray();
        }

        private RemoteWatcher.HeartbeatRsp HearthbeatRspFromProto(byte[] bytes)
        {
            var message = Proto.Msg.RemoteWatcherHeartbeatResponse.Parser.ParseFrom(bytes);
            return new RemoteWatcher.HeartbeatRsp((int)message.Uid);
        }

        //
        // RemoteScope
        //
        private byte[] RemoteScopeToProto(RemoteScope remoteScope)
        {
            var message = new Proto.Msg.RemoteScope();
            message.Node = AddressMessageBuilder(remoteScope.Address);
            return message.ToByteArray();
        }

        private RemoteScope RemoteScopeFromProto(byte[] bytes)
        {
            var message = Proto.Msg.RemoteScope.Parser.ParseFrom(bytes);
            return new RemoteScope(AddressFrom(message.Node));
        }

        //
        // DefaultResizer
        //
        private byte[] DefaultResizerToProto(DefaultResizer defaultResizer)
        {
            var message = new Proto.Msg.DefaultResizer();
            message.LowerBound = (uint)defaultResizer.LowerBound;
            message.UpperBound = (uint)defaultResizer.UpperBound;
            message.PressureThreshold = (uint)defaultResizer.PressureThreshold;
            message.RampupRate = defaultResizer.RampupRate;
            message.BackoffThreshold = defaultResizer.BackoffThreshold;
            message.BackoffRate = defaultResizer.BackoffRate;
            message.MessagesPerResize = (uint)defaultResizer.MessagesPerResize;
            return message.ToByteArray();
        }

        private DefaultResizer DefaultResizerFromProto(byte[] bytes)
        {
            var resizer = Proto.Msg.DefaultResizer.Parser.ParseFrom(bytes);
            return new DefaultResizer(
                (int)resizer.LowerBound,
                (int)resizer.UpperBound,
                (int)resizer.PressureThreshold,
                resizer.RampupRate,
                resizer.BackoffThreshold,
                resizer.BackoffRate,
                (int)resizer.MessagesPerResize);
        }

        //
        // Generic Routing Pool
        //
        private Proto.Msg.GenericRoutingPool GenericRoutingPoolBuilder(Pool roundRobinPool)
        {
            var message = new Proto.Msg.GenericRoutingPool();
            message.NrOfInstances = (uint)roundRobinPool.NrOfInstances;
            if (!string.IsNullOrEmpty(roundRobinPool.RouterDispatcher))
                message.RouterDispatcher = roundRobinPool.RouterDispatcher;
            if (roundRobinPool.Resizer != null)
                message.Resizer = PayloadToProto(roundRobinPool.Resizer);
            message.UsePoolDispatcher = roundRobinPool.UsePoolDispatcher;
            return message;
        }

        //
        // RoundRobinPool
        //
        private byte[] RoundRobinPoolToProto(RoundRobinPool roundRobinPool)
        {
            return GenericRoutingPoolBuilder(roundRobinPool).ToByteArray();
        }

        private RoundRobinPool RoundRobinPoolFromProto(byte[] bytes)
        {
            var broadcastPool = Proto.Msg.GenericRoutingPool.Parser.ParseFrom(bytes);

            Resizer resizer = broadcastPool.Resizer != null
                ? (Resizer)PayloadFrom(broadcastPool.Resizer)
                : null;

            var routerDispatcher = !string.IsNullOrEmpty(broadcastPool.RouterDispatcher)
                ? broadcastPool.RouterDispatcher
                : Dispatchers.DefaultDispatcherId;

            return new RoundRobinPool(
                (int)broadcastPool.NrOfInstances,
                resizer,
                Pool.DefaultSupervisorStrategy,
                routerDispatcher,
                broadcastPool.UsePoolDispatcher);
        }

        //
        // BroadcastPool
        //
        private byte[] BroadcastPoolToProto(BroadcastPool broadcastPool)
        {
            return GenericRoutingPoolBuilder(broadcastPool).ToByteArray();
        }

        private BroadcastPool BroadcastPoolFromProto(byte[] bytes)
        {
            var broadcastPool = Proto.Msg.GenericRoutingPool.Parser.ParseFrom(bytes);

            Resizer resizer = broadcastPool.Resizer != null 
                ? (Resizer)PayloadFrom(broadcastPool.Resizer)
                : null;
            var routerDispatcher = !string.IsNullOrEmpty(broadcastPool.RouterDispatcher)
                ? broadcastPool.RouterDispatcher
                : Dispatchers.DefaultDispatcherId;

            return new BroadcastPool(
                (int)broadcastPool.NrOfInstances,
                resizer,
                Pool.DefaultSupervisorStrategy,
                routerDispatcher,
                broadcastPool.UsePoolDispatcher);
        }

        //
        // RandomPool
        //
        private byte[] RandomPoolToProto(RandomPool randomPool)
        {
            return GenericRoutingPoolBuilder(randomPool).ToByteArray();
        }

        private RandomPool RandomPoolFromProto(byte[] bytes)
        {
            var randomPool = Proto.Msg.GenericRoutingPool.Parser.ParseFrom(bytes);

            Resizer resizer = randomPool.Resizer != null
                ? (Resizer)PayloadFrom(randomPool.Resizer)
                : null;

            var routerDispatcher = !string.IsNullOrEmpty(randomPool.RouterDispatcher)
                ? randomPool.RouterDispatcher
                : Dispatchers.DefaultDispatcherId;

            return new RandomPool(
                (int)randomPool.NrOfInstances,
                resizer,
                Pool.DefaultSupervisorStrategy,
                routerDispatcher,
                randomPool.UsePoolDispatcher);
        }

        //
        // ScatterGatherFirstCompletedPool
        //
        private byte[] ScatterGatherFirstCompletedPoolToProto(ScatterGatherFirstCompletedPool scatterGatherFirstCompletedPool)
        {
            var message = new Proto.Msg.ScatterGatherPool();
            message.Generic = GenericRoutingPoolBuilder(scatterGatherFirstCompletedPool);
            message.Within = new Proto.Msg.Timespan { Ticks = (ulong) scatterGatherFirstCompletedPool.Within.Ticks };
            return message.ToByteArray();
        }

        private ScatterGatherFirstCompletedPool ScatterGatherFirstCompletedPoolFromProto(byte[] bytes)
        {
            var scatterGatherFirstCompletedPool = Proto.Msg.ScatterGatherPool.Parser.ParseFrom(bytes);

            Resizer resizer = scatterGatherFirstCompletedPool.Generic.Resizer != null
                ? (Resizer)PayloadFrom(scatterGatherFirstCompletedPool.Generic.Resizer)
                : null;

            var routerDispatcher = !string.IsNullOrEmpty(scatterGatherFirstCompletedPool.Generic.RouterDispatcher)
                ? scatterGatherFirstCompletedPool.Generic.RouterDispatcher
                : Dispatchers.DefaultDispatcherId;

            return new ScatterGatherFirstCompletedPool(
                (int)scatterGatherFirstCompletedPool.Generic.NrOfInstances,
                resizer,
                TimeSpan.FromTicks((long)scatterGatherFirstCompletedPool.Within.Ticks),
                Pool.DefaultSupervisorStrategy,
                routerDispatcher,
                scatterGatherFirstCompletedPool.Generic.UsePoolDispatcher);
        }

        //
        // TailChoppingPool
        //
        private byte[] TailChoppingPoolToProto(TailChoppingPool tailChoppingPool)
        {
            var message = new Proto.Msg.TailChoppingPool();
            message.Generic = GenericRoutingPoolBuilder(tailChoppingPool);
            message.Within = new Proto.Msg.Timespan { Ticks = (ulong)tailChoppingPool.Within.Ticks };
            message.Interval = new Proto.Msg.Timespan { Ticks = (ulong)tailChoppingPool.Interval.Ticks };
            return message.ToByteArray();
        }

        private TailChoppingPool TailChoppingPoolFromProto(byte[] bytes)
        {
            var tailChoppingPool = Proto.Msg.TailChoppingPool.Parser.ParseFrom(bytes);

            Resizer resizer = tailChoppingPool.Generic.Resizer != null
                ? (Resizer)PayloadFrom(tailChoppingPool.Generic.Resizer)
                : null;

            var routerDispatcher = !string.IsNullOrEmpty(tailChoppingPool.Generic.RouterDispatcher)
                ? tailChoppingPool.Generic.RouterDispatcher
                : Dispatchers.DefaultDispatcherId;

            return new TailChoppingPool(
                (int)tailChoppingPool.Generic.NrOfInstances,
                resizer,
                Pool.DefaultSupervisorStrategy,
                routerDispatcher,
                TimeSpan.FromTicks((long)tailChoppingPool.Within.Ticks),
                TimeSpan.FromTicks((long)tailChoppingPool.Interval.Ticks),
                tailChoppingPool.Generic.UsePoolDispatcher);
        }

        //
        // RemoteRouterConfig
        //
        private byte[] RemoteRouterConfigToProto(RemoteRouterConfig remoteRouterConfig)
        {
            var protoRemoteRouterConfig = new Proto.Msg.RemoteRouterConfig();
            protoRemoteRouterConfig.Local = PayloadToProto(remoteRouterConfig.Local);
            protoRemoteRouterConfig.Nodes.AddRange(remoteRouterConfig.Nodes.Select(AddressMessageBuilder));
            return protoRemoteRouterConfig.ToByteArray();
        }

        private RemoteRouterConfig RemoteRouterConfigFromProto(byte[] bytes)
        {
            var protoMessage = Proto.Msg.RemoteRouterConfig.Parser.ParseFrom(bytes);
            return new RemoteRouterConfig(PayloadFrom(protoMessage.Local).AsInstanceOf<Pool>(), protoMessage.Nodes.Select(AddressFrom));
        }

        //
        // Payload
        //
        private Proto.Msg.Payload PayloadToProto(object message)
        {
            var payloadProto = new Proto.Msg.Payload();
            var serializer = system.Serialization.FindSerializerFor(message);

            // get manifest
            var manifestSerializer = serializer as SerializerWithStringManifest;
            string manifest = manifestSerializer != null ? manifestSerializer.Manifest(message) : message.GetType().TypeQualifiedName();

            payloadProto.SerializerId = serializer.Identifier;
            payloadProto.MessageManifest = ByteString.CopyFromUtf8(manifest);
            payloadProto.EnclosedMessage = ByteString.CopyFrom(serializer.ToBinary(message));
            return payloadProto;
        }

        private object PayloadFrom(Proto.Msg.Payload payload)
        {
            var manifest = !payload.MessageManifest.IsEmpty
                ? payload.MessageManifest.ToStringUtf8()
                : string.Empty;

            return system.Serialization.Deserialize(
                payload.EnclosedMessage.ToByteArray(),
                payload.SerializerId,
                manifest);
        }

        //
        // Address
        //
        private static Proto.Msg.Address AddressMessageBuilder(Address address)
        {
            var message = new Proto.Msg.Address();
            message.System = address.System;
            message.Hostname = address.Host;
            message.Port = (uint)(address.Port ?? 0);
            message.Protocol = address.Protocol;
            return message;
        }

        private static Address AddressFrom(Proto.Msg.Address addressProto)
        {
            return new Address(
                addressProto.Protocol,
                addressProto.System,
                addressProto.Hostname,
                addressProto.Port == 0 ? null : (int?)addressProto.Port);
        }

        private IActorRef ResolveActorRef(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            return system.Provider.ResolveActorRef(path);
        }
    }
}
