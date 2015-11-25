//-----------------------------------------------------------------------
// <copyright file="DaemonMsgCreateSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;
using Akka.Serialization;
using Google.ProtocolBuffers;

namespace Akka.Remote.Serialization
{
    /// <summary>
    /// This is a special <see cref="Serializer"/> that serializes and deserializes <see cref="DaemonMsgCreate"/> only.
    /// Serialization of contained <see cref="RouterConfig"/>, <see cref="Config"/>, and <see cref="Scope"/> is done with the
    /// configured serializer for those classes.
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

        /// <summary>
        /// Completely unique value to identify this implementation of Serializer, used to optimize network traffic
        /// Values from 0 to 16 is reserved for Akka internal usage
        /// </summary>
        public override int Identifier
        {
            get { return 3; }
        }

        /// <summary>
        /// Returns whether this serializer needs a manifest in the fromBinary method
        /// </summary>
        public override bool IncludeManifest
        {
            get { return false; }
        }

        private ActorRefData SerializeActorRef(IActorRef @ref)
        {
            return ActorRefData.CreateBuilder()
                .SetPath(Akka.Serialization.Serialization.SerializedActorPath(@ref))
                .Build();
        }

        private ByteString Serialize(object obj)
        {
            var serializer = system.Serialization.FindSerializerFor(obj);
            var bytes = serializer.ToBinary(obj);
            return ByteString.CopyFrom(bytes);
        }

        private object Deserialize(ByteString bytes, Type type)
        {
            var serializer = system.Serialization.FindSerializerForType(type);
            var o = serializer.FromBinary(bytes.ToByteArray(), type);
            return o;
        }

        /// <summary>
        /// Serializes the given object into a byte array
        /// </summary>
        /// <param name="obj">The object to serialize </param>
        /// <returns>A byte array containing the serialized object</returns>
        /// <exception cref="ArgumentException">Can't serialize a non-<see cref="DaemonMsgCreate"/> message using <see cref="DaemonMsgCreateSerializer"/></exception>
        public override byte[] ToBinary(object obj)
        {
            var msg = obj as DaemonMsgCreate;
            if (msg == null)
            {
                throw new ArgumentException(
                    "Can't serialize a non-DaemonMsgCreate message using DaemonMsgCreateSerializer");
            }
       
            DaemonMsgCreateData daemonBuilder = DaemonMsgCreateData.CreateBuilder()
                .SetProps(GetPropsData(msg.Props))
                .SetDeploy(GetDeployData(msg.Deploy))
                .SetPath(msg.Path)
                .SetSupervisor(SerializeActorRef(msg.Supervisor))
                .Build();

            return daemonBuilder.ToByteArray();
        }

        private PropsData GetPropsData(Props props)
        {
            var builder = PropsData.CreateBuilder()
                .SetClazz(props.Type.AssemblyQualifiedName)
                .SetDeploy(GetDeployData(props.Deploy));

            foreach (object arg in props.Arguments)
            {
                if (arg == null)
                {
                    builder = builder.AddArgs(ByteString.Empty);
                    builder = builder.AddClasses("");
                }
                else
                {
                    builder = builder.AddArgs(Serialize(arg));
                    builder = builder.AddClasses(arg.GetType().AssemblyQualifiedName);
                }
            }

            return builder.Build();
        }

        private DeployData GetDeployData(Deploy deploy)
        {
            var res = DeployData.CreateBuilder()
                .SetPath(deploy.Path);
            if (deploy.Config != ConfigurationFactory.Empty)
                res.SetConfig(Serialize(deploy.Config));
            if (deploy.RouterConfig != RouterConfig.NoRouter)
                res.SetRouterConfig(Serialize(deploy.RouterConfig));
            if (deploy.Scope != Deploy.NoScopeGiven)
                res.SetScope(Serialize(deploy.Scope));
            if (deploy.Dispatcher != Deploy.NoDispatcherGiven)
                res.SetDispatcher(deploy.Dispatcher);

            return res.Build();
        }

        /// <summary>
        /// Deserializes a byte array into an object of type <paramref name="type"/>.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="type">The type of object contained in the array</param>
        /// <returns>The object contained in the array</returns>
        /// <exception cref="TypeLoadException">
        /// Could not find type on the remote system.
        /// Ensure that the remote system has an assembly that contains the type in its assembly search path.
        /// </exception>
        public override object FromBinary(byte[] bytes, Type type)
        {
            var proto = DaemonMsgCreateData.ParseFrom(bytes);
            Type clazz; 

            try
            {
                clazz = Type.GetType(proto.Props.Clazz, true);
            }
            catch (TypeLoadException ex)
            {
                var msg = string.Format(
                       "Could not find type '{0}' on the remote system. " +
                       "Ensure that the remote system has an assembly that contains the type {0} in its assembly search path", 
                       proto.Props.Clazz);


                throw new TypeLoadException(msg, ex);
            }

            var args = GetArgs(proto);
            var props = new Props(GetDeploy(proto.Props.Deploy), clazz, args);
            return new DaemonMsgCreate(
                props,
                GetDeploy(proto.Deploy),
                proto.Path,
                DeserializeActorRef( proto.Supervisor));
        }

        private Deploy GetDeploy(DeployData protoDeploy)
        {
            Config config;
            if (protoDeploy.HasConfig)
                config = (Config) Deserialize(protoDeploy.Config, typeof (Config));
            else
                config = ConfigurationFactory.Empty;

            RouterConfig routerConfig;
            if (protoDeploy.HasRouterConfig)
                routerConfig = (RouterConfig)Deserialize(protoDeploy.RouterConfig, typeof(RouterConfig));
            else
                routerConfig = RouterConfig.NoRouter;

            Scope scope;
            if (protoDeploy.HasScope)
                scope = (Scope) Deserialize(protoDeploy.Scope, typeof (Scope));
            else
                scope = Deploy.NoScopeGiven;

            string dispatcher;
            if (protoDeploy.HasDispatcher)
                dispatcher = protoDeploy.Dispatcher;
            else 
                dispatcher = Deploy.NoDispatcherGiven;

            return new Deploy(protoDeploy.Path, config, routerConfig, scope, dispatcher);
        }

        private IEnumerable<object> GetArgs(DaemonMsgCreateData proto)
        {
            var args = new object[proto.Props.ArgsCount];
            for (int i = 0; i < args.Length; i++)
            {
                var typeName = proto.Props.GetClasses(i);
                var arg = proto.Props.GetArgs(i);
                if (typeName == "" && ByteString.Empty.Equals(arg))
                {
                    //HACK: no typename and empty arg gives null 
                    args[i] = null;
                }
                else
                {
                    Type t = null;
                    if (typeName != null)
                        t = Type.GetType(typeName);
                    args[i] = Deserialize(arg, t);
                }
            }
            return args;
        }

        private IActorRef DeserializeActorRef(ActorRefData actorRefData)
        {
            var path = actorRefData.Path;
            var @ref = system.Provider.ResolveActorRef(path);
            return @ref;
        }
    }
}
