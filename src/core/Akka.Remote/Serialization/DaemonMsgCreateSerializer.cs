using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;
using Akka.Serialization;
using Google.ProtocolBuffers;

namespace Akka.Remote.Serialization
{
    public class DaemonMsgCreateSerializer : Serializer
    {
        public DaemonMsgCreateSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier
        {
            get { return 3; }
        }

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

        public override object FromBinary(byte[] bytes, Type type)
        {
            var proto = DaemonMsgCreateData.ParseFrom(bytes);
            var clazz = Type.GetType(proto.Props.Clazz);
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
                routerConfig = (RouterConfig) Deserialize(protoDeploy.RouterConfig, protoDeploy.RouterConfig.GetType());
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