using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;
using Akka.Serialization;
using Google.ProtocolBuffers;

namespace Akka.Remote.Serialization
{
    public class DaemonMsgCreateSerializer : Serializer
    {
        public DaemonMsgCreateSerializer(ActorSystem system) : base(system)
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

        private ActorRefData SerializeActorRef(ActorRef @ref)
        {
            return ActorRefData.CreateBuilder()
                .SetPath(Akka.Serialization.Serialization.SerializedActorPath(@ref))
                .Build();
        }

        private ByteString Serialize(object obj)
        {
            Serializer serializer = system.Serialization.FindSerializerFor(obj);
            byte[] bytes = serializer.ToBinary(obj);
            return ByteString.CopyFrom(bytes);
        }

        private object Deserialize(ByteString bytes, Type type)
        {
            Serializer serializer = system.Serialization.FindSerializerForType(type);
            object o = serializer.FromBinary(bytes.ToByteArray(), type);
            return o;
        }

        public override byte[] ToBinary(object obj)
        {
            if (!(obj is DaemonMsgCreate))
            {
                throw new ArgumentException(
                    "Can't serialize a non-DaemonMsgCreate message using DaemonMsgCreateSerializer");
            }

            var msg = (DaemonMsgCreate) obj;            
            Props props = msg.Props;
            Deploy deploy = msg.Deploy;

            Func<Deploy, DeployData> deployProto = d =>
            {
                DeployData.Builder res = DeployData.CreateBuilder()
                    .SetPath(d.Path);
                if (d.Config != ConfigurationFactory.Empty)
                    res.SetConfig(Serialize(d.Config));
                if (d.RouterConfig != RouterConfig.NoRouter)
                    res.SetRouterConfig(Serialize(d.RouterConfig));
                if (d.Scope != Deploy.NoScopeGiven)
                    res.SetScope(Serialize(d.Scope));
                if (d.Dispatcher != Deploy.NoDispatcherGiven)
                    res.SetDispatcher(d.Dispatcher);

                return res.Build();
            };

            Func<PropsData> propsProto = () =>
            {
                PropsData.Builder builder = PropsData.CreateBuilder()
                    .SetClazz(props.Type.AssemblyQualifiedName)
                    .SetDeploy(deployProto(props.Deploy));

                foreach (object arg in props.Arguments)
                {
                    builder = builder.AddArgs(Serialize(arg));
                    //TODO: deal with null?
                    builder = builder.AddClasses(arg.GetType().AssemblyQualifiedName);
                }

                return builder.Build();
            };

            /*
 DaemonMsgCreateData.newBuilder.
        setProps(propsProto).
        setDeploy(deployProto(deploy)).
        setPath(path).
        setSupervisor(serializeActorRef(supervisor)).
        build.toByteArray
*/
            DaemonMsgCreateData daemonBuilder = DaemonMsgCreateData.CreateBuilder()
                .SetProps(propsProto())
                .SetDeploy(deployProto(msg.Deploy))
                .SetPath(msg.Path)
                .SetSupervisor(SerializeActorRef(msg.Supervisor))
                .Build();

            return daemonBuilder.ToByteArray();
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            DaemonMsgCreateData proto = DaemonMsgCreateData.ParseFrom(bytes);

            Func<DeployData, Deploy> deploy = protoDeploy =>
            {
                Config config = null;
                if (protoDeploy.HasConfig)
                    config = (Config) Deserialize(protoDeploy.Config, typeof (Config));
                else
                    config = ConfigurationFactory.Empty;

                RouterConfig routerConfig = null;
                if (protoDeploy.HasRouterConfig)
                    routerConfig = (RouterConfig) Deserialize(protoDeploy.RouterConfig, typeof (RouterConfig));
                else
                    routerConfig = RouterConfig.NoRouter;

                Scope scope = null;
                if (protoDeploy.HasScope)
                    scope = (Scope) Deserialize(protoDeploy.Scope, typeof (Scope));
                else
                    scope = Deploy.NoScopeGiven;

                string dispatcher = null;
                if (protoDeploy.HasDispatcher)
                    dispatcher = protoDeploy.Dispatcher;
                else
                    dispatcher = Deploy.NoDispatcherGiven; 

                return new Deploy(protoDeploy.Path, config, routerConfig, scope, dispatcher);
            };

            Type clazz = Type.GetType(proto.Props.Clazz);

            var args = new object[] {};
            //  val args: Vector[AnyRef] = (proto.getProps.getArgsList.asScala zip proto.getProps.getClassesList.asScala)
            //    .map(p ⇒ deserialize(p._1, system.dynamicAccess.getClassFor[AnyRef](p._2).get))(collection.breakOut)
            var props = new Props(deploy(proto.Props.Deploy), clazz, args);


            return new DaemonMsgCreate(
                props,
                deploy(proto.Deploy),
                proto.Path,
                DeserializeActorRef(system, proto.Supervisor));
        }

        private ActorRef DeserializeActorRef(ActorSystem system, ActorRefData actorRefData)
        {
            string path = actorRefData.Path;
            ActorRef @ref = system.Provider.ResolveActorRef(path);
            return @ref;
        }
    }
}