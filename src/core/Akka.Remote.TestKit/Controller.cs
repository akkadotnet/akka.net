using System;
using Akka.Actor;

namespace Akka.Remote.TestKit
{
    /// <summary>
    /// This controls test execution by managing barriers (delegated to
    /// <see cref="BarrierCoordinator"/>, its child) and allowing
    /// network and other failures to be injected at the test nodes.
    /// 
    /// INTERNAL API.
    /// </summary>
    class Controller : UntypedActor
    {
        public sealed class ClientDisconnected
        {
            private readonly RoleName _name;

            public ClientDisconnected(RoleName name)
            {
                _name = name;
            }

            public RoleName Name
            {
                get { return _name; }
            }

            private bool Equals(ClientDisconnected other)
            {
                return Equals(_name, other._name);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is ClientDisconnected && Equals((ClientDisconnected) obj);
            }

            public override int GetHashCode()
            {
                return (_name != null ? _name.GetHashCode() : 0);
            }

            public static bool operator ==(ClientDisconnected left, ClientDisconnected right)
            {
                return Equals(left, right);
            }

            public static bool operator !=(ClientDisconnected left, ClientDisconnected right)
            {
                return !Equals(left, right);
            }
        }

        public class ClientDisconnectedException : AkkaException
        {
            public ClientDisconnectedException(string msg) : base(msg){}
        }

        public class GetNodes
        {
            private GetNodes() { }
            private static readonly GetNodes _instance = new GetNodes();

            public static GetNodes Instance
            {
                get
                {
                    return _instance;
                }
            }
        }

        //TODO: Necessary?
        public class GetSockAddr
        {
            private GetSockAddr() { }
            private static readonly GetSockAddr _instance = new GetSockAddr();

            public static GetSockAddr Instance
            {
                get
                {
                    return _instance;
                }
            }
        }

        /// <summary>
        /// Marker interface for working with <see cref="BarrierCoordinator"/>
        /// </summary>
        internal interface IHaveNodeInfo
        {
            NodeInfo Node { get; }
        }

        internal sealed class NodeInfo
        {
            readonly RoleName _name;
            readonly Address _addr;
            readonly ActorRef _fsm;

            public NodeInfo(RoleName name, Address addr, ActorRef fsm)
            {
                _name = name;
                _addr = addr;
                _fsm = fsm;
            }

            public RoleName Name
            {
                get { return _name; }
            }

            public Address Addr
            {
                get { return _addr; }
            }

            public ActorRef FSM
            {
                get { return _fsm; }
            }

            bool Equals(NodeInfo other)
            {
                return Equals(_name, other._name) && Equals(_addr, other._addr) && Equals(_fsm, other._fsm);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is NodeInfo && Equals((NodeInfo) obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = (_name != null ? _name.GetHashCode() : 0);
                    hashCode = (hashCode*397) ^ (_addr != null ? _addr.GetHashCode() : 0);
                    hashCode = (hashCode*397) ^ (_fsm != null ? _fsm.GetHashCode() : 0);
                    return hashCode;
                }
            }

            public static bool operator ==(NodeInfo left, NodeInfo right)
            {
                return Equals(left, right);
            }

            public static bool operator !=(NodeInfo left, NodeInfo right)
            {
                return !Equals(left, right);
            }
        }

        readonly TestConductorSettings _settings = TestConductor.Get(Context.System).Settings;

        protected override void OnReceive(object message)
        {
            throw new NotImplementedException();
        }
    }
}