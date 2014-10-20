using System;
using System.Collections.Generic;
using Akka.Remote.Transport;
using Akka.Util;
using Address = Akka.Actor.Address;

namespace Akka.Remote.TestKit
{
    public sealed class RoleName
    {
        readonly string _name;

        public RoleName(string name)
        {
            _name = name;
        }

        bool Equals(RoleName other)
        {
            return string.Equals(_name, other._name);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((RoleName)obj);
        }

        public override int GetHashCode()
        {
            return (_name != null ? _name.GetHashCode() : 0);
        }

        public static bool operator ==(RoleName left, RoleName right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(RoleName left, RoleName right)
        {
            return !Equals(left, right);
        }

        public string Name
        {
            get { return _name; }
        }

        public override string ToString()
        {
            return string.Format("RoleName({0})", _name);
        }
    }

    //TODO: This is messy, better way to do this?
    //Marker interface to avoid using reflection to work out if message
    //is derived from generic type
    interface IToClient{}

    class ToClient<T> : IToClient where T : IClientOp, INetworkOp
    {
        private readonly T _msg;

        public ToClient(T msg)
        {
            _msg = msg;
        }

        public T Msg
        {
            get { return _msg; }
        }

        protected bool Equals(ToClient<T> other)
        {
            return EqualityComparer<T>.Default.Equals(_msg, other._msg);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((ToClient<T>)obj);
        }

        public override int GetHashCode()
        {
            return EqualityComparer<T>.Default.GetHashCode(_msg);
        }

        public static bool operator ==(ToClient<T> left, ToClient<T> right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(ToClient<T> left, ToClient<T> right)
        {
            return !Equals(left, right);
        }
    }

    //TODO: This is messy, better way to do this?
    //Marker interface to avoid using reflection to work out if message
    //is derived from generic type
    interface IToServer{}

    class ToServer<T> : IToServer where T : IServerOp, INetworkOp
    {
        readonly T _msg;

        public ToServer(T msg)
        {
            _msg = msg;
        }

        public T Msg
        {
            get { return _msg; }
        }

        protected bool Equals(ToServer<T> other)
        {
            return EqualityComparer<T>.Default.Equals(_msg, other._msg);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((ToServer<T>)obj);
        }

        public override int GetHashCode()
        {
            return EqualityComparer<T>.Default.GetHashCode(_msg);
        }

        public static bool operator ==(ToServer<T> left, ToServer<T> right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(ToServer<T> left, ToServer<T> right)
        {
            return !Equals(left, right);
        }
    }

    interface IClientOp { } // messages sent to from Conductor to Player
    interface IServerOp { } // messages sent to from Player to Conductor
    interface ICommandOp { } // messages sent from TestConductorExt to Conductor
    interface INetworkOp { } // messages sent over the wire
    interface IUnconfirmedClientOp : IClientOp { } // unconfirmed messages going to the Player
    interface IConfirmedClientOp : IClientOp { }

    /// <summary>
    /// First message of connection sets names straight.
    /// </summary>
    sealed class Hello : INetworkOp
    {
        readonly string _name;
        readonly Address _address;

        private bool Equals(Hello other)
        {
            return string.Equals(_name, other._name) && Equals(_address, other._address);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Hello && Equals((Hello)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((_name != null ? _name.GetHashCode() : 0) * 397) ^ (_address != null ? _address.GetHashCode() : 0);
            }
        }

        public static bool operator ==(Hello left, Hello right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(Hello left, Hello right)
        {
            return !Equals(left, right);
        }

        public Hello(string name, Address address)
        {
            _name = name;
            _address = address;
        }

        public string Name
        {
            get { return _name; }
        }

        public Address Address
        {
            get { return _address; }
        }
    }

    sealed class EnterBarrier : IServerOp, INetworkOp
    {
        readonly string _name;
        readonly TimeSpan? _timeout;

        public EnterBarrier(string name, TimeSpan? timeout)
        {
            _name = name;
            _timeout = timeout;
        }

        private bool Equals(EnterBarrier other)
        {
            return string.Equals(_name, other._name) && _timeout.Equals(other._timeout);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is EnterBarrier && Equals((EnterBarrier)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((_name != null ? _name.GetHashCode() : 0) * 397) ^ _timeout.GetHashCode();
            }
        }

        public static bool operator ==(EnterBarrier left, EnterBarrier right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(EnterBarrier left, EnterBarrier right)
        {
            return !Equals(left, right);
        }

        public string Name
        {
            get { return _name; }
        }

        public TimeSpan? Timeout
        {
            get { return _timeout; }
        }
    }

    sealed class FailBarrier : IServerOp, INetworkOp
    {
        readonly string _name;

        public FailBarrier(string name)
        {
            _name = name;
        }

        public string Name
        {
            get { return _name; }
        }

        private bool Equals(FailBarrier other)
        {
            return string.Equals(_name, other._name);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is FailBarrier && Equals((FailBarrier)obj);
        }

        public override int GetHashCode()
        {
            return (_name != null ? _name.GetHashCode() : 0);
        }

        public static bool operator ==(FailBarrier left, FailBarrier right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(FailBarrier left, FailBarrier right)
        {
            return !Equals(left, right);
        }
    }

    sealed class BarrierResult : IUnconfirmedClientOp, INetworkOp
    {
        readonly string _name;
        readonly bool _success;

        public BarrierResult(string name, bool success)
        {
            _name = name;
            _success = success;
        }

        public string Name
        {
            get { return _name; }
        }

        public bool Success
        {
            get { return _success; }
        }

        bool Equals(BarrierResult other)
        {
            return string.Equals(_name, other._name) && _success.Equals(other._success);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is BarrierResult && Equals((BarrierResult)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((_name != null ? _name.GetHashCode() : 0) * 397) ^ _success.GetHashCode();
            }
        }

        public static bool operator ==(BarrierResult left, BarrierResult right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(BarrierResult left, BarrierResult right)
        {
            return !Equals(left, right);
        }
    }

    sealed class Throttle : ICommandOp
    {
        readonly RoleName _node;
        readonly RoleName _target;
        readonly ThrottleTransportAdapter.Direction _direction;
        readonly float _rateMBit;

        public Throttle(RoleName node, RoleName target, ThrottleTransportAdapter.Direction direction, float rateMBit)
        {
            _node = node;
            _target = target;
            _direction = direction;
            _rateMBit = rateMBit;
        }

        public RoleName Node
        {
            get { return _node; }
        }

        public RoleName Target
        {
            get { return _target; }
        }

        public ThrottleTransportAdapter.Direction Direction
        {
            get { return _direction; }
        }

        public float RateMBit
        {
            get { return _rateMBit; }
        }

        bool Equals(Throttle other)
        {
            return Equals(_node, other._node) && Equals(_target, other._target) && Equals(_direction, other._direction) && _rateMBit.Equals(other._rateMBit);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Throttle && Equals((Throttle)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (_node != null ? _node.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (_target != null ? _target.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (_direction != null ? _direction.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ _rateMBit.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(Throttle left, Throttle right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(Throttle left, Throttle right)
        {
            return !Equals(left, right);
        }
    }

    sealed class ThrottleMsg : IConfirmedClientOp, INetworkOp
    {
        readonly Address _target;
        readonly ThrottleTransportAdapter.Direction _direction;
        readonly float _rateMBit;

        public ThrottleMsg(Address target, ThrottleTransportAdapter.Direction direction, float rateMBit)
        {
            _target = target;
            _direction = direction;
            _rateMBit = rateMBit;
        }

        public Address Target
        {
            get { return _target; }
        }

        public ThrottleTransportAdapter.Direction Direction
        {
            get { return _direction; }
        }

        public float RateMBit
        {
            get { return _rateMBit; }
        }

        bool Equals(ThrottleMsg other)
        {
            return Equals(_target, other._target) && Equals(_direction, other._direction) && _rateMBit.Equals(other._rateMBit);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is ThrottleMsg && Equals((ThrottleMsg)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (_target != null ? _target.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (_direction != null ? _direction.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ _rateMBit.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(ThrottleMsg left, ThrottleMsg right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(ThrottleMsg left, ThrottleMsg right)
        {
            return !Equals(left, right);
        }
    }

    sealed class Disconnect : ICommandOp
    {
        readonly RoleName _node;
        readonly RoleName _target;
        readonly bool _abort;

        public Disconnect(RoleName node, RoleName target, bool abort)
        {
            _node = node;
            _target = target;
            _abort = abort;
        }

        public RoleName Node
        {
            get { return _node; }
        }

        public RoleName Target
        {
            get { return _target; }
        }

        public bool Abort
        {
            get { return _abort; }
        }

        bool Equals(Disconnect other)
        {
            return Equals(_node, other._node) && Equals(_target, other._target) && _abort.Equals(other._abort);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Disconnect && Equals((Disconnect)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (_node != null ? _node.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (_target != null ? _target.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ _abort.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(Disconnect left, Disconnect right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(Disconnect left, Disconnect right)
        {
            return !Equals(left, right);
        }
    }

    sealed class DisconnectMsg : IConfirmedClientOp, INetworkOp
    {
        readonly Address _target;
        readonly bool _abort;

        public DisconnectMsg(Address target, bool abort)
        {
            _target = target;
            _abort = abort;
        }

        public Address Target
        {
            get { return _target; }
        }

        public bool Abort
        {
            get { return _abort; }
        }

        bool Equals(DisconnectMsg other)
        {
            return Equals(_target, other._target) && _abort.Equals(other._abort);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is DisconnectMsg && Equals((DisconnectMsg)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((_target != null ? _target.GetHashCode() : 0) * 397) ^ _abort.GetHashCode();
            }
        }

        public static bool operator ==(DisconnectMsg left, DisconnectMsg right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(DisconnectMsg left, DisconnectMsg right)
        {
            return !Equals(left, right);
        }
    }

    sealed class Terminate : ICommandOp
    {
        readonly RoleName _node;
        readonly Either<bool, int> _shutdownOrExit;

        public Terminate(RoleName node, Either<bool, int> shutdownOrExit)
        {
            _node = node;
            _shutdownOrExit = shutdownOrExit;
        }

        public RoleName Node
        {
            get { return _node; }
        }

        public Either<bool, int> ShutdownOrExit
        {
            get { return _shutdownOrExit; }
        }

        bool Equals(Terminate other)
        {
            return Equals(_node, other._node) && Equals(_shutdownOrExit, other._shutdownOrExit);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Terminate && Equals((Terminate)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((_node != null ? _node.GetHashCode() : 0) * 397) ^ (_shutdownOrExit != null ? _shutdownOrExit.GetHashCode() : 0);
            }
        }

        public static bool operator ==(Terminate left, Terminate right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(Terminate left, Terminate right)
        {
            return !Equals(left, right);
        }
    }

    sealed class TerminateMsg : IConfirmedClientOp, INetworkOp
    {
        readonly Either<bool, int> _shutdownOrExit;

        public TerminateMsg(Either<bool, int> shutdownOrExit)
        {
            _shutdownOrExit = shutdownOrExit;
        }

        public Either<bool, int> ShutdownOrExit
        {
            get { return _shutdownOrExit; }
        }

        bool Equals(TerminateMsg other)
        {
            return Equals(_shutdownOrExit, other._shutdownOrExit);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is TerminateMsg && Equals((TerminateMsg)obj);
        }

        public override int GetHashCode()
        {
            return (_shutdownOrExit != null ? _shutdownOrExit.GetHashCode() : 0);
        }

        public static bool operator ==(TerminateMsg left, TerminateMsg right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(TerminateMsg left, TerminateMsg right)
        {
            return !Equals(left, right);
        }
    }

    sealed class GetAddress : IServerOp, INetworkOp
    {
        readonly RoleName _node;

        public GetAddress(RoleName node)
        {
            _node = node;
        }

        public RoleName Node
        {
            get { return _node; }
        }

        bool Equals(GetAddress other)
        {
            return Equals(_node, other._node);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is GetAddress && Equals((GetAddress)obj);
        }

        public override int GetHashCode()
        {
            return (_node != null ? _node.GetHashCode() : 0);
        }

        public static bool operator ==(GetAddress left, GetAddress right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(GetAddress left, GetAddress right)
        {
            return !Equals(left, right);
        }
    }

    sealed class AddressReply : IUnconfirmedClientOp, INetworkOp
    {
        readonly RoleName _node;
        readonly Address _addr;

        public AddressReply(RoleName node, Address addr)
        {
            _node = node;
            _addr = addr;
        }

        public RoleName Node
        {
            get { return _node; }
        }

        public Address Addr
        {
            get { return _addr; }
        }

        bool Equals(AddressReply other)
        {
            return Equals(_node, other._node) && Equals(_addr, other._addr);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is AddressReply && Equals((AddressReply)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((_node != null ? _node.GetHashCode() : 0) * 397) ^ (_addr != null ? _addr.GetHashCode() : 0);
            }
        }

        public static bool operator ==(AddressReply left, AddressReply right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(AddressReply left, AddressReply right)
        {
            return !Equals(left, right);
        }
    }

    public class Done : IServerOp, IUnconfirmedClientOp, INetworkOp
    {
        private Done() { }
        private static readonly Done _instance = new Done();

        public static Done Instance
        {
            get
            {
                return _instance;
            }
        }
    }

    sealed class Remove : ICommandOp
    {
        readonly RoleName _node;

        public Remove(RoleName node)
        {
            _node = node;
        }

        public RoleName Node
        {
            get { return _node; }
        }

        bool Equals(Remove other)
        {
            return Equals(_node, other._node);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Remove && Equals((Remove)obj);
        }

        public override int GetHashCode()
        {
            return (_node != null ? _node.GetHashCode() : 0);
        }

        public static bool operator ==(Remove left, Remove right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(Remove left, Remove right)
        {
            return !Equals(left, right);
        }
    }
}
