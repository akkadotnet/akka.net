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
using Akka.Remote.Routing;
using Akka.Routing;
using Akka.Serialization;
using Akka.Util;
using Akka.Util.Internal;
using Google.Protobuf;

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
        private const string BroadcastPoolManifest = "ROBP";
        private const string RandomPoolManifest = "RORP";
        private const string RoundRobinPoolManifest = "RORRP";
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
            if (obj is LocalScope) return EmptyBytes;
            if (obj is RemoteRouterConfig) return RemoteRouterConfigToProto((RemoteRouterConfig)obj);

            throw new ArgumentException($"Cannot serialize object of type [${obj.GetType().Name}]");
        }

        public override string Manifest(object obj)
        {
            if (obj is Identify) return IdentifyManifest;
            if (obj is ActorIdentity) return ActorIdentityManifest;
            if (obj is PoisonPill) return PoisonPillManifest;
            if (obj is Kill) return KillManifest;
            if (obj is RemoteWatcher.Heartbeat) return RemoteWatcherHearthbeatManifest;
            if (obj is LocalScope) return LocalScopeManifest;
            if (obj is RemoteRouterConfig) return RemoteRouterConfigManifest;

            throw new ArgumentException($"Cannot serialize object of type [${obj.GetType().Name}]");
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            if (manifest == IdentifyManifest) return IdentifyFromProto(bytes);
            if (manifest == ActorIdentityManifest) return ActorIdentityFromProto(bytes);
            if (manifest == PoisonPillManifest) return PoisonPill.Instance;
            if (manifest == KillManifest) return Kill.Instance;
            if (manifest == RemoteWatcherHearthbeatManifest) return RemoteWatcher.Heartbeat.Instance;
            if (manifest == LocalScopeManifest) return LocalScope.Instance;
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
            payloadProto.SerializerId = serializer.Identifier;
            payloadProto.MessageManifest = ByteString.CopyFromUtf8(GetObjectManifest(serializer, message));
            payloadProto.EnclosedMessage = ByteString.CopyFrom(serializer.ToBinary(message));
            return payloadProto;
        }

        private object PayloadFrom(Proto.Msg.Payload payload)
        {
            return system.Serialization.Deserialize(payload.EnclosedMessage.ToByteArray(), payload.SerializerId, payload.MessageManifest.ToString());
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string GetObjectManifest(Serializer serializer, object obj)
        {
            var manifestSerializer = serializer as SerializerWithStringManifest;
            if (manifestSerializer != null)
            {
                return manifestSerializer.Manifest(obj);
            }

            return obj.GetType().TypeQualifiedName();
        }

        private IActorRef ResolveActorRef(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            return system.Provider.ResolveActorRef(path);
        }
    }
}
