//-----------------------------------------------------------------------
// <copyright file="MiscMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Remote.Routing;
using Akka.Routing;
using Akka.Serialization;
using Akka.Util;
using Akka.Util.Internal;
using Google.Protobuf;

namespace Akka.Remote.Serialization
{
    public sealed class MiscMessageSerializer : SerializerWithStringManifest
    {
        private const string IdentifyManifest = "ID";
        private const string ActorIdentityManifest = "AID";
        private const string ActorRefManifest = "AR";
        private const string PoisonPillManifest = "PP";
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
        private const string ConsistentHashingPoolManifest = "ROCHP";
        private const string RemoteRouterConfigManifest = "RORRC";
        private const string StatusFailureManifest = "STF";
        private const string StatusSuccessManifest = "STS";

        private static readonly byte[] EmptyBytes = Array.Empty<byte>();

        private readonly WrappedPayloadSupport _payloadSupport;
        private readonly ExceptionSupport _exceptionSupport;

        /// <summary>
        /// Initializes a new instance of the <see cref="MiscMessageSerializer" /> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        public MiscMessageSerializer(ExtendedActorSystem system) : base(system)
        {
            _payloadSupport = new WrappedPayloadSupport(system);
            _exceptionSupport = new ExceptionSupport(system);
        }

        /// <inheritdoc />
        public override byte[] ToBinary(object obj)
        {
            switch (obj)
            {
                case Identify identify:
                    return IdentifyToProto(identify);
                case ActorIdentity actorIdentity:
                    return ActorIdentityToProto(actorIdentity);
                case IActorRef actorRef:
                    return ActorRefToProto(actorRef);
                case PoisonPill _:
                case Kill _:
                case RemoteWatcher.Heartbeat _:
                    return EmptyBytes;
                case RemoteWatcher.HeartbeatRsp heartbeatRsp:
                    return HeartbeatRspToProto(heartbeatRsp);
                case Status.Success success:
                    return StatusSuccessToProto(success); 
                case Status.Failure failure:
                    return StatusFailureToProto(failure);
                case LocalScope _:
                    return EmptyBytes;
                case RemoteScope remoteScope:
                    return RemoteScopeToProto(remoteScope);
                case Config config:
                    return ConfigToProto(config);
                case FromConfig fromConfig:
                    return FromConfigToProto(fromConfig);
                case DefaultResizer defaultResizer:
                    return DefaultResizerToProto(defaultResizer);
                case RoundRobinPool roundRobinPool:
                    return RoundRobinPoolToProto(roundRobinPool);
                case BroadcastPool broadcastPool:
                    return BroadcastPoolToProto(broadcastPool);
                case RandomPool randomPool:
                    return RandomPoolToProto(randomPool);
                case ScatterGatherFirstCompletedPool scatterPool:
                    return ScatterGatherFirstCompletedPoolToProto(scatterPool);
                case TailChoppingPool tailChoppingPool:
                    return TailChoppingPoolToProto(tailChoppingPool);
                case ConsistentHashingPool hashingPool:
                    return ConsistentHashingPoolToProto(hashingPool);
                case RemoteRouterConfig remoteRouterConfig:
                    return RemoteRouterConfigToProto(remoteRouterConfig);
                default:
                    throw new ArgumentException($"Cannot serialize object of type [{obj.GetType().TypeQualifiedName()}]");
            }
        }

        /// <inheritdoc />
        public override string Manifest(object obj)
        {
            switch (obj)
            {
                case Identify _:
                    return IdentifyManifest;
                case ActorIdentity _:
                    return ActorIdentityManifest;
                case IActorRef _:
                    return ActorRefManifest;
                case PoisonPill _:
                    return PoisonPillManifest;
                case Kill _:
                    return KillManifest;
                case RemoteWatcher.Heartbeat _:
                    return RemoteWatcherHearthbeatManifest;
                case RemoteWatcher.HeartbeatRsp _:
                    return RemoteWatcherHearthbeatRspManifest;
                case Status.Success _:
                    return StatusSuccessManifest;
                case Status.Failure _:
                    return StatusFailureManifest;
                case LocalScope _:
                    return LocalScopeManifest;
                case RemoteScope _:
                    return RemoteScopeManifest;
                case Config _:
                    return ConfigManifest;
                case FromConfig _:
                    return FromConfigManifest;
                case DefaultResizer _:
                    return DefaultResizerManifest;
                case RoundRobinPool _:
                    return RoundRobinPoolManifest;
                case BroadcastPool _:
                    return BroadcastPoolManifest;
                case RandomPool _:
                    return RandomPoolManifest;
                case ScatterGatherFirstCompletedPool _:
                    return ScatterGatherPoolManifest;
                case TailChoppingPool _:
                    return TailChoppingPoolManifest;
                case ConsistentHashingPool _:
                    return ConsistentHashingPoolManifest;
                case RemoteRouterConfig _:
                    return RemoteRouterConfigManifest;
                default:
                    throw new ArgumentException($"Cannot deserialize object of type [{obj.GetType().TypeQualifiedName()}]");
            }
        }

        /// <inheritdoc />
        public override object FromBinary(byte[] bytes, string manifest)
        {
            switch (manifest)
            {
                case IdentifyManifest:
                    return IdentifyFromProto(bytes);
                case ActorIdentityManifest:
                    return ActorIdentityFromProto(bytes);
                case ActorRefManifest:
                    return ActorRefFromProto(bytes);
                case PoisonPillManifest:
                    return PoisonPill.Instance;
                case KillManifest:
                    return Kill.Instance;
                case RemoteWatcherHearthbeatManifest:
                    return RemoteWatcher.Heartbeat.Instance;
                case RemoteWatcherHearthbeatRspManifest:
                    return HearthbeatRspFromProto(bytes);
                case StatusSuccessManifest:
                    return StatusSuccessFromProto(bytes);
                case StatusFailureManifest:
                    return StatusFailureFromProto(bytes);
                case LocalScopeManifest:
                    return LocalScope.Instance;
                case RemoteScopeManifest:
                    return RemoteScopeFromProto(bytes);
                case ConfigManifest:
                    return ConfigFromProto(bytes);
                case FromConfigManifest:
                    return FromConfigFromProto(bytes);
                case DefaultResizerManifest:
                    return DefaultResizerFromProto(bytes);
                case RoundRobinPoolManifest:
                    return RoundRobinPoolFromProto(bytes);
                case BroadcastPoolManifest:
                    return BroadcastPoolFromProto(bytes);
                case RandomPoolManifest:
                    return RandomPoolFromProto(bytes);
                case ScatterGatherPoolManifest:
                    return ScatterGatherFirstCompletedPoolFromProto(bytes);
                case TailChoppingPoolManifest:
                    return TailChoppingPoolFromProto(bytes);
                case ConsistentHashingPoolManifest:
                    return ConsistentHashingPoolFromProto(bytes);
                case RemoteRouterConfigManifest:
                    return RemoteRouterConfigFromProto(bytes);
                default:
                    throw new SerializationException($"Unimplemented deserialization of message with manifest [{manifest}] in [{nameof(MiscMessageSerializer)}]");
            }
        }

        //
        // Identify
        //
        private byte[] IdentifyToProto(Identify identify)
        {
            var protoIdentify = new Proto.Msg.Identify();
            if (identify.MessageId != null)
                protoIdentify.MessageId = _payloadSupport.PayloadToProto(identify.MessageId);
            return protoIdentify.ToByteArray();
        }

        private Identify IdentifyFromProto(byte[] bytes)
        {
            var protoMessage = Proto.Msg.Identify.Parser.ParseFrom(bytes);
            if (protoMessage.MessageId == null)
                return new Identify(null);
            return new Identify(_payloadSupport.PayloadFrom(protoMessage.MessageId));
        }

        //
        // ActorIdentity
        //
        private byte[] ActorIdentityToProto(ActorIdentity actorIdentity)
        {
            var protoIdentify = new Proto.Msg.ActorIdentity();
            protoIdentify.CorrelationId = _payloadSupport.PayloadToProto(actorIdentity.MessageId);
            protoIdentify.Path = Akka.Serialization.Serialization.SerializedActorPath(actorIdentity.Subject);
            return protoIdentify.ToByteArray();
        }

        private ActorIdentity ActorIdentityFromProto(byte[] bytes)
        {
            var protoMessage = Proto.Msg.ActorIdentity.Parser.ParseFrom(bytes);
            return new ActorIdentity(_payloadSupport.PayloadFrom(protoMessage.CorrelationId), ResolveActorRef(protoMessage.Path));
        }

        //
        // IActorRef
        //
        private byte[] ActorRefToProto(IActorRef actorRef)
        {
            var protoActor = new Proto.Msg.ActorRefData();
            if (actorRef is Nobody) // TODO: this is a hack. Should work without it
                protoActor.Path = "nobody";
            else
                protoActor.Path = Akka.Serialization.Serialization.SerializedActorPath(actorRef);
            return protoActor.ToByteArray();
        }

        private IActorRef ActorRefFromProto(byte[] bytes)
        {
            var protoMessage = Proto.Msg.ActorRefData.Parser.ParseFrom(bytes);
            if (protoMessage.Path.Equals("nobody"))
                return Nobody.Instance;
            return system.AsInstanceOf<ExtendedActorSystem>().Provider.ResolveActorRef(protoMessage.Path);
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
        // Status.Success
        //
        
        private byte[] StatusSuccessToProto(Status.Success success)
        {
            var message = new Proto.Msg.StatusSuccess();
            message.Status = _payloadSupport.PayloadToProto(success.Status);
            return message.ToByteArray();
        }
        
        private Status.Success StatusSuccessFromProto(byte[] bytes)
        {
            var message = Proto.Msg.StatusSuccess.Parser.ParseFrom(bytes);
            return new Status.Success(_payloadSupport.PayloadFrom(message.Status));
        }
        
        //
        // Status.Failure
        //
        
        private byte[] StatusFailureToProto(Status.Failure failure)
        {
            var message = new Proto.Msg.StatusFailure();
            message.Cause = _exceptionSupport.ExceptionToProto(failure.Cause);
            message.State = _payloadSupport.PayloadToProto(failure.State);
            return message.ToByteArray();
        }
        
        private Status.Failure StatusFailureFromProto(byte[] bytes)
        {
            var message = Proto.Msg.StatusFailure.Parser.ParseFrom(bytes);
            var payload = _payloadSupport.PayloadFrom(message.State);
            return new Status.Failure(_exceptionSupport.ExceptionFromProto(message.Cause), payload);
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
        // Config
        //
        private byte[] ConfigToProto(Config config)
        {
            if (config.IsEmpty)
                return EmptyBytes;

            return Encoding.UTF8.GetBytes(config.Root.ToString());
        }

        private Config ConfigFromProto(byte[] bytes)
        {
            if (bytes.Length == 0)
                return Config.Empty;

            return ConfigurationFactory.ParseString(Encoding.UTF8.GetString(bytes));
        }

        //
        // FromConfig
        //
        private byte[] FromConfigToProto(FromConfig fromConfig)
        {
            if (fromConfig == FromConfig.Instance)
                return EmptyBytes;

            var message = new Proto.Msg.FromConfig();

            if (fromConfig.Resizer != null)
                message.Resizer = _payloadSupport.PayloadToProto(fromConfig.Resizer);

            if (!string.IsNullOrEmpty(fromConfig.RouterDispatcher))
                message.RouterDispatcher = fromConfig.RouterDispatcher;

            return message.ToByteArray();
        }

        private FromConfig FromConfigFromProto(byte[] bytes)
        {
            if (bytes.Length == 0)
                return FromConfig.Instance;

            var fromConfig = Proto.Msg.FromConfig.Parser.ParseFrom(bytes);

            Resizer resizer = fromConfig.Resizer != null
                ? (Resizer)_payloadSupport.PayloadFrom(fromConfig.Resizer)
                : null;

            var routerDispatcher = !string.IsNullOrEmpty(fromConfig.RouterDispatcher)
                ? fromConfig.RouterDispatcher
                : Dispatchers.DefaultDispatcherId;

            return new FromConfig(resizer, Pool.DefaultSupervisorStrategy, routerDispatcher);
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
        private Proto.Msg.GenericRoutingPool GenericRoutingPoolBuilder(Pool pool)
        {
            var message = new Proto.Msg.GenericRoutingPool();
            message.NrOfInstances = (uint)pool.NrOfInstances;
            if (!string.IsNullOrEmpty(pool.RouterDispatcher))
                message.RouterDispatcher = pool.RouterDispatcher;
            if (pool.Resizer != null)
                message.Resizer = _payloadSupport.PayloadToProto(pool.Resizer);
            message.UsePoolDispatcher = pool.UsePoolDispatcher;
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
                ? (Resizer)_payloadSupport.PayloadFrom(broadcastPool.Resizer)
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
                ? (Resizer)_payloadSupport.PayloadFrom(broadcastPool.Resizer)
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
                ? (Resizer)_payloadSupport.PayloadFrom(randomPool.Resizer)
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
            message.Within = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(scatterGatherFirstCompletedPool.Within);
            return message.ToByteArray();
        }

        private ScatterGatherFirstCompletedPool ScatterGatherFirstCompletedPoolFromProto(byte[] bytes)
        {
            var scatterGatherFirstCompletedPool = Proto.Msg.ScatterGatherPool.Parser.ParseFrom(bytes);

            Resizer resizer = scatterGatherFirstCompletedPool.Generic.Resizer != null
                ? (Resizer)_payloadSupport.PayloadFrom(scatterGatherFirstCompletedPool.Generic.Resizer)
                : null;

            var routerDispatcher = !string.IsNullOrEmpty(scatterGatherFirstCompletedPool.Generic.RouterDispatcher)
                ? scatterGatherFirstCompletedPool.Generic.RouterDispatcher
                : Dispatchers.DefaultDispatcherId;

            return new ScatterGatherFirstCompletedPool(
                (int)scatterGatherFirstCompletedPool.Generic.NrOfInstances,
                resizer,
                scatterGatherFirstCompletedPool.Within.ToTimeSpan(),
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
            message.Within = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(tailChoppingPool.Within);
            message.Interval = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(tailChoppingPool.Interval);
            return message.ToByteArray();
        }

        private TailChoppingPool TailChoppingPoolFromProto(byte[] bytes)
        {
            var tailChoppingPool = Proto.Msg.TailChoppingPool.Parser.ParseFrom(bytes);

            Resizer resizer = tailChoppingPool.Generic.Resizer != null
                ? (Resizer)_payloadSupport.PayloadFrom(tailChoppingPool.Generic.Resizer)
                : null;

            var routerDispatcher = !string.IsNullOrEmpty(tailChoppingPool.Generic.RouterDispatcher)
                ? tailChoppingPool.Generic.RouterDispatcher
                : Dispatchers.DefaultDispatcherId;

            return new TailChoppingPool(
                (int)tailChoppingPool.Generic.NrOfInstances,
                resizer,
                Pool.DefaultSupervisorStrategy,
                routerDispatcher,
                tailChoppingPool.Within.ToTimeSpan(),
                tailChoppingPool.Interval.ToTimeSpan(),
                tailChoppingPool.Generic.UsePoolDispatcher);
        }

        //
        // ConsistentHashingPool
        //
        private byte[] ConsistentHashingPoolToProto(ConsistentHashingPool hashingPool)
        {
            return GenericRoutingPoolBuilder(hashingPool).ToByteArray();
        }

        private object ConsistentHashingPoolFromProto(byte[] bytes)
        {
            var consistentHashingPool = Proto.Msg.GenericRoutingPool.Parser.ParseFrom(bytes);

            Resizer resizer = consistentHashingPool.Resizer != null
                ? (Resizer)_payloadSupport.PayloadFrom(consistentHashingPool.Resizer)
                : null;
            var routerDispatcher = !string.IsNullOrEmpty(consistentHashingPool.RouterDispatcher)
                ? consistentHashingPool.RouterDispatcher
                : Dispatchers.DefaultDispatcherId;

            return new ConsistentHashingPool(
                (int)consistentHashingPool.NrOfInstances,
                resizer,
                Pool.DefaultSupervisorStrategy,
                routerDispatcher,
                consistentHashingPool.UsePoolDispatcher);
        }

        //
        // RemoteRouterConfig
        //
        private byte[] RemoteRouterConfigToProto(RemoteRouterConfig remoteRouterConfig)
        {
            var protoRemoteRouterConfig = new Proto.Msg.RemoteRouterConfig();
            protoRemoteRouterConfig.Local = _payloadSupport.PayloadToProto(remoteRouterConfig.Local);
            protoRemoteRouterConfig.Nodes.AddRange(remoteRouterConfig.Nodes.Select(AddressMessageBuilder));
            return protoRemoteRouterConfig.ToByteArray();
        }

        private RemoteRouterConfig RemoteRouterConfigFromProto(byte[] bytes)
        {
            var protoMessage = Proto.Msg.RemoteRouterConfig.Parser.ParseFrom(bytes);
            return new RemoteRouterConfig(_payloadSupport.PayloadFrom(protoMessage.Local).AsInstanceOf<Pool>(), protoMessage.Nodes.Select(AddressFrom));
        }

        //
        // Address
        //
        private static Proto.Msg.AddressData AddressMessageBuilder(Address address)
        {
            var message = new Proto.Msg.AddressData();
            message.System = address.System;
            message.Hostname = address.Host;
            message.Port = (uint)(address.Port ?? 0);
            message.Protocol = address.Protocol;
            return message;
        }

        private static Address AddressFrom(Proto.Msg.AddressData addressProto)
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
