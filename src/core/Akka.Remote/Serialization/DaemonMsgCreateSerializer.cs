//-----------------------------------------------------------------------
// <copyright file="DaemonMsgCreateSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;
using Akka.Serialization;
using Akka.Util;
using Akka.Util.Internal;
using Google.Protobuf;

namespace Akka.Remote.Serialization
{
    /// <summary>
    /// Serializes Akka's internal <see cref="DaemonMsgCreate"/> using protobuf.
    /// </summary>
    public class DaemonMsgCreateSerializer : Serializer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DaemonMsgCreateSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        public DaemonMsgCreateSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        /// <inheritdoc />
        public override bool IncludeManifest => false;

        /// <inheritdoc />
        public override byte[] ToBinary(object obj)
        {
            if (obj is DaemonMsgCreate msg)
            {
                var message = new Proto.Msg.DaemonMsgCreateData();
                message.Props = PropsToProto(msg.Props);
                message.Deploy = DeployToProto(msg.Deploy);
                message.Path = msg.Path;
                message.Supervisor = SerializeActorRef(msg.Supervisor);

                return message.ToByteArray();
            }

            throw new ArgumentException($"Can't serialize a non-DaemonMsgCreate message using DaemonMsgCreateSerializer [{obj.GetType()}]");
        }

        /// <inheritdoc />
        public override object FromBinary(byte[] bytes, Type type)
        {
            var proto = Proto.Msg.DaemonMsgCreateData.Parser.ParseFrom(bytes);

            return new DaemonMsgCreate(
                PropsFromProto(proto.Props),
                DeployFromProto(proto.Deploy),
                proto.Path,
                DeserializeActorRef(proto.Supervisor));
        }

        //
        // Props
        //
        private Proto.Msg.PropsData PropsToProto(Props props)
        {
            var propsBuilder = new Proto.Msg.PropsData();
            propsBuilder.Clazz = props.Type.TypeQualifiedName();
            propsBuilder.Deploy = DeployToProto(props.Deploy);
            foreach (var arg in props.Arguments)
            {
                var tuple = Serialize(arg);

                propsBuilder.Args.Add(ByteString.CopyFrom(tuple.Item4));
                propsBuilder.Manifests.Add(tuple.Item3);
                propsBuilder.SerializerIds.Add(tuple.Item1);
                propsBuilder.HasManifest.Add(tuple.Item2);
            }

            return propsBuilder;
        }

        private Props PropsFromProto(Proto.Msg.PropsData protoProps)
        {
            var actorClass = Type.GetType(protoProps.Clazz);
            var args = new object[protoProps.Args.Count];
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = system.Serialization.Deserialize(
                    protoProps.Args[i].ToByteArray(),
                    protoProps.SerializerIds[i],
                    protoProps.Manifests[i]
                );
            }

            return new Props(DeployFromProto(protoProps.Deploy), actorClass, args);
        }

        //
        // Deploy
        //
        private Proto.Msg.DeployData DeployToProto(Deploy deploy)
        {
            var deployBuilder = new Proto.Msg.DeployData();
            deployBuilder.Path = deploy.Path;

            {
                var tuple = Serialize(deploy.Config);
                deployBuilder.ConfigSerializerId = tuple.Item1;
                deployBuilder.ConfigManifest = tuple.Item3;
                deployBuilder.Config = ByteString.CopyFrom(tuple.Item4);
            }

            if (deploy.RouterConfig != NoRouter.Instance)
            {
                var tuple = Serialize(deploy.RouterConfig);
                deployBuilder.RouterConfigSerializerId = tuple.Item1;
                deployBuilder.RouterConfigManifest = tuple.Item3;
                deployBuilder.RouterConfig = ByteString.CopyFrom(tuple.Item4);
            }

            if (deploy.Scope != Deploy.NoScopeGiven)
            {
                var tuple = Serialize(deploy.Scope);
                deployBuilder.ScopeSerializerId = tuple.Item1;
                deployBuilder.ScopeManifest = tuple.Item3;
                deployBuilder.Scope = ByteString.CopyFrom(tuple.Item4);
            }

            if (deploy.Dispatcher != Deploy.NoDispatcherGiven)
            {
                deployBuilder.Dispatcher = deploy.Dispatcher;
            }

            return deployBuilder;
        }

        private Deploy DeployFromProto(Proto.Msg.DeployData protoDeploy)
        {
            Config config;
            if (protoDeploy.ConfigSerializerId > 0) // TODO: should be protoDeploy.Config != null. But it always not null
            {
                config = system.Serialization.Deserialize(
                    protoDeploy.Config.ToByteArray(),
                    protoDeploy.ConfigSerializerId,
                    protoDeploy.ConfigManifest).AsInstanceOf<Config>();
            }
            else
            {
                config = Config.Empty;
            }

            
            RouterConfig routerConfig;
            if (protoDeploy.RouterConfigSerializerId > 0) // TODO: should be protoDeploy.RouterConfig != null. But it always not null
            {
                routerConfig = system.Serialization.Deserialize(
                    protoDeploy.RouterConfig.ToByteArray(),
                    protoDeploy.RouterConfigSerializerId,
                    protoDeploy.RouterConfigManifest).AsInstanceOf<RouterConfig>();
            }
            else
            {
                routerConfig = NoRouter.Instance;
            }

            Scope scope;
            if (protoDeploy.ScopeSerializerId > 0) // TODO: should be protoDeploy.Scope != null. But it always not null
            {
                scope = system.Serialization.Deserialize(
                    protoDeploy.Scope.ToByteArray(),
                    protoDeploy.ScopeSerializerId,
                    protoDeploy.ScopeManifest).AsInstanceOf<Scope>();
            }
            else
            {
                scope = Deploy.NoScopeGiven;
            }

            var dispatcher = !string.IsNullOrEmpty(protoDeploy.Dispatcher)
                ? protoDeploy.Dispatcher
                : Deploy.NoDispatcherGiven;

            return new Deploy(protoDeploy.Path, config, routerConfig, scope, dispatcher);
        }

        //
        // IActorRef
        //
        private Proto.Msg.ActorRefData SerializeActorRef(IActorRef actorRef)
        {
            return new Proto.Msg.ActorRefData
            {
                Path = Akka.Serialization.Serialization.SerializedActorPath(actorRef)
            };
        }

        private IActorRef DeserializeActorRef(Proto.Msg.ActorRefData actorRefData)
        {
            return system.Provider.ResolveActorRef(actorRefData.Path);
        }

        private (int, bool, string, byte[]) Serialize(object obj)
        {
            var serializer = system.Serialization.FindSerializerFor(obj);

            var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, obj);
            var hasManifest = !string.IsNullOrEmpty(manifest);

            return (serializer.Identifier, hasManifest, manifest, serializer.ToBinary(obj));
        }
    }
}
