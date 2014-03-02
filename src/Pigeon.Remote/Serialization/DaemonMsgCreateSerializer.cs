using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;
using Akka.Serialization;
using Google.ProtocolBuffers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Remote.Serialization
{
    public class DaemonMsgCreateSerializer : Serializer
    {
        public DaemonMsgCreateSerializer(ActorSystem system) : base(system) { }

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
            return null;
        }
        private ByteString Serialize(object obj)
        {
            var serializer = this.system.Serialization.FindSerializerFor(obj);
            var bytes= serializer.ToBinary(obj);
            return ByteString.CopyFrom(bytes);
        }

        public override byte[] ToBinary(object obj)
        {
            if (!(obj is DaemonMsgCreate))
            {
                throw new ArgumentException("Can't serialize a non-DaemonMsgCreate message using DaemonMsgCreateSerializer");
            }

            var msg = (DaemonMsgCreate)obj;
            var props = msg.Props;
            var deploy = msg.Deploy;

            Func<Deploy, DeployData> deployProto = d =>
            {
                var res = DeployData.CreateBuilder()
                .SetPath(d.Path);
                if (d.Config != ConfigurationFactory.Empty)
                    res = res.SetConfig(Serialize(d.Config));
                if (d.RouterConfig != RouterConfig.NoRouter)
                    res = res.SetRouterConfig(Serialize(d.RouterConfig));
                if (d.Scope != Deploy.NoScopeGiven)
                    res = res.SetScope(Serialize(d.Scope));
                if (d.Dispatcher != Deploy.NoDispatcherGiven)
                    res = res.SetDispatcher(d.Dispatcher);

                return res.Build();
            };

            Func<PropsData> propsProto = () => {
                var builder = PropsData.CreateBuilder()
                .SetClazz(props.Type.AssemblyQualifiedName)
                .SetDeploy(deployProto(props.Deploy));

                foreach (var arg in props.Arguments)
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
            var daemonBuilder = DaemonMsgCreateData.CreateBuilder()
                .SetProps(propsProto())
                .SetDeploy(deployProto(msg.Deploy))
                .SetPath(msg.Path)
                .SetSupervisor(SerializeActorRef(msg.Supervisor))
                .Build();

            return daemonBuilder.ToByteArray();
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            throw new NotImplementedException();
        }
    }
}
