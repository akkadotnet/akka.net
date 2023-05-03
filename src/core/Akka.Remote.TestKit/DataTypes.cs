//-----------------------------------------------------------------------
// <copyright file="DataTypes.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Remote.Transport;
using Akka.Util;
using Address = Akka.Actor.Address;

namespace Akka.Remote.TestKit
{
    public sealed class RoleName : IEquatable<RoleName>
    {
        public RoleName(string name)
        {
            Name = name;
        }

        public bool Equals(RoleName other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(Name, other.Name);
        }

        
        public override bool Equals(object obj) => obj is RoleName role && Equals(role);

        
        public override int GetHashCode() => (Name != null ? Name.GetHashCode() : 0);

        /// <summary>
        /// Compares two specified <see cref="RoleName"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="RoleName"/> used for comparison</param>
        /// <param name="right">The second <see cref="RoleName"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="RoleName"/> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(RoleName left, RoleName right) => Equals(left, right);

        /// <summary>
        /// Compares two specified <see cref="RoleName"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="RoleName"/> used for comparison</param>
        /// <param name="right">The second <see cref="RoleName"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="RoleName"/> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(RoleName left, RoleName right) => !Equals(left, right);

        public string Name { get; }

        
        public override string ToString() => $"RoleName({Name})";
    }

    //TODO: This is messy, better way to do this?
    //Marker interface to avoid using reflection to work out if message
    //is derived from generic type
    interface IToClient
    {
        object Msg { get; }
    }

    class ToClient<T> : IToClient where T : IClientOp, INetworkOp
    {
        public ToClient(T msg)
        {
            Msg = msg;
        }

        object IToClient.Msg
        {
            get { return Msg; }
        }

        public T Msg { get; }


        protected bool Equals(ToClient<T> other)
        {
            return EqualityComparer<T>.Default.Equals(Msg, other.Msg);
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
            return EqualityComparer<T>.Default.GetHashCode(Msg);
        }

        /// <summary>
        /// Compares two specified <see cref="ToClient{T}"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="ToClient{T}"/> used for comparison</param>
        /// <param name="right">The second <see cref="ToClient{T}"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="ToClient{T}"/> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(ToClient<T> left, ToClient<T> right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified <see cref="ToClient{T}"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="ToClient{T}"/> used for comparison</param>
        /// <param name="right">The second <see cref="ToClient{T}"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="ToClient{T}"/> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(ToClient<T> left, ToClient<T> right)
        {
            return !Equals(left, right);
        }
    }

    //TODO: This is messy, better way to do this?
    //Marker interface to avoid using reflection to work out if message
    //is derived from generic type
    interface IToServer
    {
        object Msg { get; }
    }

    class ToServer<T> : IToServer where T : IServerOp, INetworkOp
    {
        public ToServer(T msg)
        {
            Msg = msg;
        }

        public T Msg { get; }

        object IToServer.Msg
        {
            get { return Msg; }
        }

        
        protected bool Equals(ToServer<T> other)
        {
            return EqualityComparer<T>.Default.Equals(Msg, other.Msg);
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
            return EqualityComparer<T>.Default.GetHashCode(Msg);
        }

        /// <summary>
        /// Compares two specified <see cref="ToServer{T}"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="ToServer{T}"/> used for comparison</param>
        /// <param name="right">The second <see cref="ToServer{T}"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="ToServer{T}"/> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(ToServer<T> left, ToServer<T> right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified <see cref="ToServer{T}"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="ToServer{T}"/> used for comparison</param>
        /// <param name="right">The second <see cref="ToServer{T}"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="ToServer{T}"/> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(ToServer<T> left, ToServer<T> right)
        {
            return !Equals(left, right);
        }
    }

    /// <summary>
    /// messages sent to from Conductor to Player
    /// </summary>
    interface IClientOp { } 

    /// <summary>
    /// messages sent to from Player to Conductor
    /// </summary>
    interface IServerOp { }

    /// <summary>
    /// messages sent from TestConductorExt to Conductor
    /// </summary>
    interface ICommandOp { }

    /// <summary>
    ///  messages sent over the wire
    /// </summary> 
    interface INetworkOp { }

    /// <summary>
    /// unconfirmed messages going to the Player
    /// </summary>
    interface IUnconfirmedClientOp : IClientOp { }
    interface IConfirmedClientOp : IClientOp { }

    /// <summary>
    /// First message of connection sets names straight.
    /// </summary>
    sealed class Hello : INetworkOp
    {
        private bool Equals(Hello other)
        {
            return string.Equals(Name, other.Name) && Equals(Address, other.Address);
        }

        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Hello hello && Equals(hello);
        }

        
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Name != null ? Name.GetHashCode() : 0) * 397) ^ (Address != null ? Address.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// Compares two specified <see cref="Hello"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="Hello"/> used for comparison</param>
        /// <param name="right">The second <see cref="Hello"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="Hello"/> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(Hello left, Hello right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified <see cref="Hello"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="Hello"/> used for comparison</param>
        /// <param name="right">The second <see cref="Hello"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="Hello"/> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(Hello left, Hello right)
        {
            return !Equals(left, right);
        }

        public Hello(string name, Address address)
        {
            Name = name;
            Address = address;
        }

        public string Name { get; }

        public Address Address { get; }
    }

    sealed class EnterBarrier : IServerOp, INetworkOp
    {
        public EnterBarrier(string name, TimeSpan? timeout)
        {
            Name = name;
            Timeout = timeout;
        }

        private bool Equals(EnterBarrier other)
        {
            return string.Equals(Name, other.Name) && Timeout.Equals(other.Timeout);
        }

        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is EnterBarrier barrier && Equals(barrier);
        }

        
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Name != null ? Name.GetHashCode() : 0) * 397) ^ Timeout.GetHashCode();
            }
        }

        /// <summary>
        /// Compares two specified <see cref="EnterBarrier"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="EnterBarrier"/> used for comparison</param>
        /// <param name="right">The second <see cref="EnterBarrier"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="EnterBarrier"/> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(EnterBarrier left, EnterBarrier right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified <see cref="EnterBarrier"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="EnterBarrier"/> used for comparison</param>
        /// <param name="right">The second <see cref="EnterBarrier"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="EnterBarrier"/> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(EnterBarrier left, EnterBarrier right)
        {
            return !Equals(left, right);
        }

        public string Name { get; }

        public TimeSpan? Timeout { get; }
    }

    sealed class FailBarrier : IServerOp, INetworkOp
    {
        public FailBarrier(string name)
        {
            Name = name;
        }

        public string Name { get; }

        private bool Equals(FailBarrier other)
        {
            return string.Equals(Name, other.Name);
        }

        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is FailBarrier barrier && Equals(barrier);
        }

        
        public override int GetHashCode()
        {
            return (Name != null ? Name.GetHashCode() : 0);
        }

        /// <summary>
        /// Compares two specified <see cref="FailBarrier"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="FailBarrier"/> used for comparison</param>
        /// <param name="right">The second <see cref="FailBarrier"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="FailBarrier"/> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(FailBarrier left, FailBarrier right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified <see cref="FailBarrier"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="FailBarrier"/> used for comparison</param>
        /// <param name="right">The second <see cref="FailBarrier"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="FailBarrier"/> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(FailBarrier left, FailBarrier right)
        {
            return !Equals(left, right);
        }
    }

    sealed class BarrierResult : IUnconfirmedClientOp, INetworkOp
    {
        public BarrierResult(string name, bool success)
        {
            Name = name;
            Success = success;
        }

        public string Name { get; }

        public bool Success { get; }

        bool Equals(BarrierResult other)
        {
            return string.Equals(Name, other.Name) && Success.Equals(other.Success);
        }

        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is BarrierResult result && Equals(result);
        }

        
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Name != null ? Name.GetHashCode() : 0) * 397) ^ Success.GetHashCode();
            }
        }

        /// <summary>
        /// Compares two specified <see cref="BarrierResult"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="BarrierResult"/> used for comparison</param>
        /// <param name="right">The second <see cref="BarrierResult"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="BarrierResult"/> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(BarrierResult left, BarrierResult right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified <see cref="BarrierResult"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="BarrierResult"/> used for comparison</param>
        /// <param name="right">The second <see cref="BarrierResult"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="BarrierResult"/> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(BarrierResult left, BarrierResult right)
        {
            return !Equals(left, right);
        }
    }

    sealed class Throttle : ICommandOp
    {
        public Throttle(RoleName node, RoleName target, ThrottleTransportAdapter.Direction direction, float rateMBit)
        {
            Node = node;
            Target = target;
            Direction = direction;
            RateMBit = rateMBit;
        }

        public RoleName Node { get; }

        public RoleName Target { get; }

        public ThrottleTransportAdapter.Direction Direction { get; }

        public float RateMBit { get; }

        bool Equals(Throttle other)
        {
            return Equals(Node, other.Node) && Equals(Target, other.Target) && Equals(Direction, other.Direction) && RateMBit.Equals(other.RateMBit);
        }

        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Throttle throttle && Equals(throttle);
        }

        
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (Node != null ? Node.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Target != null ? Target.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Direction.GetHashCode();
                hashCode = (hashCode * 397) ^ RateMBit.GetHashCode();
                return hashCode;
            }
        }

        /// <summary>
        /// Compares two specified <see cref="Throttle"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="Throttle"/> used for comparison</param>
        /// <param name="right">The second <see cref="Throttle"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="Throttle"/> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(Throttle left, Throttle right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified <see cref="Throttle"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="Throttle"/> used for comparison</param>
        /// <param name="right">The second <see cref="Throttle"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="Throttle"/> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(Throttle left, Throttle right)
        {
            return !Equals(left, right);
        }
    }

    sealed class ThrottleMsg : IConfirmedClientOp, INetworkOp
    {
        public ThrottleMsg(Address target, ThrottleTransportAdapter.Direction direction, float rateMBit)
        {
            Target = target;
            Direction = direction;
            RateMBit = rateMBit;
        }

        public Address Target { get; }

        public ThrottleTransportAdapter.Direction Direction { get; }

        public float RateMBit { get; }

        bool Equals(ThrottleMsg other)
        {
            return Equals(Target, other.Target) && Equals(Direction, other.Direction) && RateMBit.Equals(other.RateMBit);
        }

        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is ThrottleMsg msg && Equals(msg);
        }

        
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Target != null ? Target.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Direction.GetHashCode();
                hashCode = (hashCode * 397) ^ RateMBit.GetHashCode();
                return hashCode;
            }
        }

        /// <summary>
        /// Compares two specified <see cref="ThrottleMsg"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="ThrottleMsg"/> used for comparison</param>
        /// <param name="right">The second <see cref="ThrottleMsg"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="ThrottleMsg"/> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(ThrottleMsg left, ThrottleMsg right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified <see cref="ThrottleMsg"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="ThrottleMsg"/> used for comparison</param>
        /// <param name="right">The second <see cref="ThrottleMsg"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="ThrottleMsg"/> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(ThrottleMsg left, ThrottleMsg right)
        {
            return !Equals(left, right);
        }
    }

    sealed class Disconnect : ICommandOp
    {
        public Disconnect(RoleName node, RoleName target, bool abort)
        {
            Node = node;
            Target = target;
            Abort = abort;
        }

        public RoleName Node { get; }

        public RoleName Target { get; }

        public bool Abort { get; }

        bool Equals(Disconnect other)
        {
            return Equals(Node, other.Node) && Equals(Target, other.Target) && Abort.Equals(other.Abort);
        }

        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Disconnect disconnect && Equals(disconnect);
        }

        
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (Node != null ? Node.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Target != null ? Target.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Abort.GetHashCode();
                return hashCode;
            }
        }

        /// <summary>
        /// Compares two specified <see cref="Disconnect"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="Disconnect"/> used for comparison</param>
        /// <param name="right">The second <see cref="Disconnect"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="Disconnect"/> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(Disconnect left, Disconnect right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified <see cref="Disconnect"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="Disconnect"/> used for comparison</param>
        /// <param name="right">The second <see cref="Disconnect"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="Disconnect"/> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(Disconnect left, Disconnect right)
        {
            return !Equals(left, right);
        }
    }

    sealed class DisconnectMsg : IConfirmedClientOp, INetworkOp
    {
        public DisconnectMsg(Address target, bool abort)
        {
            Target = target;
            Abort = abort;
        }

        public Address Target { get; }

        public bool Abort { get; }

        bool Equals(DisconnectMsg other)
        {
            return Equals(Target, other.Target) && Abort.Equals(other.Abort);
        }

        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is DisconnectMsg msg && Equals(msg);
        }

        
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Target != null ? Target.GetHashCode() : 0) * 397) ^ Abort.GetHashCode();
            }
        }

        /// <summary>
        /// Compares two specified <see cref="DisconnectMsg"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="DisconnectMsg"/> used for comparison</param>
        /// <param name="right">The second <see cref="DisconnectMsg"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="DisconnectMsg"/> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(DisconnectMsg left, DisconnectMsg right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified <see cref="DisconnectMsg"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="DisconnectMsg"/> used for comparison</param>
        /// <param name="right">The second <see cref="DisconnectMsg"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="DisconnectMsg"/> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(DisconnectMsg left, DisconnectMsg right)
        {
            return !Equals(left, right);
        }
    }

    sealed class Terminate : ICommandOp
    {
        public Terminate(RoleName node, Either<bool, int> shutdownOrExit)
        {
            Node = node;
            ShutdownOrExit = shutdownOrExit;
        }

        public RoleName Node { get; }

        public Either<bool, int> ShutdownOrExit { get; }

        bool Equals(Terminate other)
        {
            return Equals(Node, other.Node) && Equals(ShutdownOrExit, other.ShutdownOrExit);
        }

        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Terminate terminate && Equals(terminate);
        }

        
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Node != null ? Node.GetHashCode() : 0) * 397) ^ (ShutdownOrExit != null ? ShutdownOrExit.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// Compares two specified <see cref="Terminate"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="Terminate"/> used for comparison</param>
        /// <param name="right">The second <see cref="Terminate"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="Terminate"/> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(Terminate left, Terminate right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified <see cref="Terminate"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="Terminate"/> used for comparison</param>
        /// <param name="right">The second <see cref="Terminate"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="Terminate"/> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(Terminate left, Terminate right)
        {
            return !Equals(left, right);
        }
    }

    sealed class TerminateMsg : IConfirmedClientOp, INetworkOp
    {
        public TerminateMsg(Either<bool, int> shutdownOrExit)
        {
            ShutdownOrExit = shutdownOrExit;
        }

        public Either<bool, int> ShutdownOrExit { get; }

        bool Equals(TerminateMsg other)
        {
            return Equals(ShutdownOrExit, other.ShutdownOrExit);
        }

        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is TerminateMsg msg && Equals(msg);
        }

        
        public override int GetHashCode()
        {
            return (ShutdownOrExit != null ? ShutdownOrExit.GetHashCode() : 0);
        }

        /// <summary>
        /// Compares two specified <see cref="TerminateMsg"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="TerminateMsg"/> used for comparison</param>
        /// <param name="right">The second <see cref="TerminateMsg"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="TerminateMsg"/> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(TerminateMsg left, TerminateMsg right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified <see cref="TerminateMsg"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="TerminateMsg"/> used for comparison</param>
        /// <param name="right">The second <see cref="TerminateMsg"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="TerminateMsg"/> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(TerminateMsg left, TerminateMsg right)
        {
            return !Equals(left, right);
        }
    }

    sealed class GetAddress : IServerOp, INetworkOp
    {
        public GetAddress(RoleName node)
        {
            Node = node;
        }

        public RoleName Node { get; }

        bool Equals(GetAddress other)
        {
            return Equals(Node, other.Node);
        }

        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is GetAddress address && Equals(address);
        }

        
        public override int GetHashCode()
        {
            return (Node != null ? Node.GetHashCode() : 0);
        }

        /// <summary>
        /// Compares two specified <see cref="GetAddress"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="GetAddress"/> used for comparison</param>
        /// <param name="right">The second <see cref="GetAddress"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="GetAddress"/> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(GetAddress left, GetAddress right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified <see cref="GetAddress"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="GetAddress"/> used for comparison</param>
        /// <param name="right">The second <see cref="GetAddress"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="GetAddress"/> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(GetAddress left, GetAddress right)
        {
            return !Equals(left, right);
        }
    }

    sealed class AddressReply : IUnconfirmedClientOp, INetworkOp
    {
        public AddressReply(RoleName node, Address addr)
        {
            Node = node;
            Addr = addr;
        }

        public RoleName Node { get; }

        public Address Addr { get; }

        bool Equals(AddressReply other)
        {
            return Equals(Node, other.Node) && Equals(Addr, other.Addr);
        }

        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is AddressReply reply && Equals(reply);
        }

        
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Node != null ? Node.GetHashCode() : 0) * 397) ^ (Addr != null ? Addr.GetHashCode() : 0);
            }
        }

        /// <summary>
        /// Compares two specified <see cref="AddressReply"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="AddressReply"/> used for comparison</param>
        /// <param name="right">The second <see cref="AddressReply"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="AddressReply"/> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(AddressReply left, AddressReply right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified <see cref="AddressReply"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="AddressReply"/> used for comparison</param>
        /// <param name="right">The second <see cref="AddressReply"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="AddressReply"/> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(AddressReply left, AddressReply right)
        {
            return !Equals(left, right);
        }
    }

    public class Done : IServerOp, IUnconfirmedClientOp, INetworkOp
    {
        private Done() { }

        public static Done Instance { get; } = new Done();
    }

    sealed class Remove : ICommandOp
    {
        public Remove(RoleName node)
        {
            Node = node;
        }

        public RoleName Node { get; }

        bool Equals(Remove other)
        {
            return Equals(Node, other.Node);
        }

        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Remove remove && Equals(remove);
        }

        
        public override int GetHashCode()
        {
            return (Node != null ? Node.GetHashCode() : 0);
        }

        /// <summary>
        /// Compares two specified <see cref="Remove"/> for equality.
        /// </summary>
        /// <param name="left">The first <see cref="Remove"/> used for comparison</param>
        /// <param name="right">The second <see cref="Remove"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="Remove"/> are equal; otherwise <c>false</c></returns>
        public static bool operator ==(Remove left, Remove right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified <see cref="Remove"/> for inequality.
        /// </summary>
        /// <param name="left">The first <see cref="Remove"/> used for comparison</param>
        /// <param name="right">The second <see cref="Remove"/> used for comparison</param>
        /// <returns><c>true</c> if both <see cref="Remove"/> are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(Remove left, Remove right)
        {
            return !Equals(left, right);
        }
    }
}

