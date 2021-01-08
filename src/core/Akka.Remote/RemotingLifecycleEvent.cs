//-----------------------------------------------------------------------
// <copyright file="RemotingLifecycleEvent.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Util.Internal;

namespace Akka.Remote
{
    /// <summary>
    /// Remote lifecycle events that are published to the <see cref="EventStream"/> when
    /// initialization / connect / disconnect events that occur during network operations
    /// </summary>
    public abstract class RemotingLifecycleEvent
    {
        /// <summary>
        ///     Logs the level.
        /// </summary>
        /// <returns>LogLevel.</returns>
        public abstract LogLevel LogLevel();
    }

    /// <summary>
    /// TBD
    /// </summary>
    public abstract class AssociationEvent : RemotingLifecycleEvent
    {
        /// <summary>
        /// TBD
        /// </summary>
        public abstract Address LocalAddress { get; protected set; }
        /// <summary>
        /// TBD
        /// </summary>
        public abstract Address RemoteAddress { get; protected set; }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract bool IsInbound { get; protected set; }

        /// <summary>
        /// TBD
        /// </summary>
        protected string EventName;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            var networkDirection = IsInbound ? "<-" : "->";
            return string.Format("{0} [{1}] {2} {3}", EventName, LocalAddress, networkDirection, RemoteAddress);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class AssociatedEvent : AssociationEvent
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.DebugLevel;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override Address LocalAddress { get; protected set; }
        /// <summary>
        /// TBD
        /// </summary>
        public override Address RemoteAddress { get; protected set; }
        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsInbound { get; protected set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="localAddress">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        /// <param name="inbound">TBD</param>
        public AssociatedEvent(Address localAddress, Address remoteAddress, bool inbound)
        {
            LocalAddress = localAddress;
            RemoteAddress = remoteAddress;
            IsInbound = inbound;
            EventName = "Associated";
        }
    }

    /// <summary>
    /// Event that is fired when a remote association to another <see cref="ActorSystem"/> is terminated.
    /// </summary>
    public sealed class DisassociatedEvent : AssociationEvent
    {
        /// <inheritdoc/>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.DebugLevel;
        }

        /// <inheritdoc/>
        public override Address LocalAddress { get; protected set; }

        /// <inheritdoc/>
        public override Address RemoteAddress { get; protected set; }

        /// <inheritdoc/>
        public override bool IsInbound { get; protected set; }

        /// <summary>
        /// Creates a new <see cref="DisassociatedEvent"/> instance.
        /// </summary>
        /// <param name="localAddress">The address of the current actor system.</param>
        /// <param name="remoteAddress">The address of the remote actor system.</param>
        /// <param name="inbound"><c>true</c> if this side of the connection as inbound, <c>false</c> if it was outbound.</param>
        public DisassociatedEvent(Address localAddress, Address remoteAddress, bool inbound)
        {
            LocalAddress = localAddress;
            RemoteAddress = remoteAddress;
            IsInbound = inbound;
            EventName = "Disassociated";
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class AssociationErrorEvent : AssociationEvent
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        /// <param name="localAddress">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        /// <param name="inbound">TBD</param>
        /// <param name="level">TBD</param>
        public AssociationErrorEvent(Exception cause, Address localAddress, Address remoteAddress, bool inbound, LogLevel level)
        {
            LocalAddress = localAddress;
            RemoteAddress = remoteAddress;
            IsInbound = inbound;
            EventName = "AssociationError";
            _level = level;
            Cause = cause;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Exception Cause { get; private set; }

        private readonly LogLevel _level;
        /// <summary>
        /// TBD
        /// </summary>
		/// <returns>TBD</returns>
        public override LogLevel LogLevel()
        {
            return _level;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override Address LocalAddress { get; protected set; }
        /// <summary>
        /// TBD
        /// </summary>
        public override Address RemoteAddress { get; protected set; }
        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsInbound { get; protected set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return string.Format("{0}: Error [{1}] [{2}]", base.ToString(), Cause.Message, Cause.StackTrace);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class RemotingListenEvent : RemotingLifecycleEvent
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="listenAddresses">TBD</param>
        public RemotingListenEvent(IList<Address> listenAddresses)
        {
            ListenAddresses = listenAddresses;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IList<Address> ListenAddresses { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.InfoLevel;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return string.Format("Remoting now listens on addresses: [{0}]",
                ListenAddresses.Select(x => x.ToString()).Join(","));
        }
    }

    /// <summary>
    /// Event that is published when the remoting system terminates.
    /// </summary>
    public sealed class RemotingShutdownEvent : RemotingLifecycleEvent
    {
        /// <inheritdoc/>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.InfoLevel;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return "Remoting shut down";
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class RemotingErrorEvent : RemotingLifecycleEvent
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        public RemotingErrorEvent(Exception cause)
        {
            Cause = cause;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Exception Cause { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.ErrorLevel;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return string.Format("Remoting error: [{0}] [{1}]", Cause.Message, Cause.StackTrace);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class QuarantinedEvent : RemotingLifecycleEvent
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="address">TBD</param>
        /// <param name="uid">TBD</param>
        public QuarantinedEvent(Address address, int uid)
        {
            Uid = uid;
            Address = address;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Address Address { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public int Uid { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.WarningLevel;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return
                string.Format(
                    "Association to [{0}] having UID [{1}] is irrecoverably failed. UID is now quarantined and all " +
                    "messages to this UID will be delivered to dead letters. Remote actorsystem must be restarted to recover " +
                    "from this situation.", Address, Uid);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class ThisActorSystemQuarantinedEvent : RemotingLifecycleEvent
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="localAddress">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        public ThisActorSystemQuarantinedEvent(Address localAddress, Address remoteAddress)
        {
            LocalAddress = localAddress;
            RemoteAddress = remoteAddress;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Address LocalAddress { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public Address RemoteAddress { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.WarningLevel;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return string.Format("The remote system {0} has quarantined this system {1}.", RemoteAddress, LocalAddress);
        }
    }

    /// <summary>
    /// INTERNAL API.
    /// 
    /// Used for publishing remote lifecycle events to the <see cref="EventStream"/> of the provided <see cref="ActorSystem"/>.
    /// </summary>
    internal class EventPublisher
    {
        /// <summary>
        /// TBD
        /// </summary>
        public ActorSystem System { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public ILoggingAdapter Log { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly LogLevel LogLevel;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="log">TBD</param>
        /// <param name="logLevel">TBD</param>
        public EventPublisher(ActorSystem system, ILoggingAdapter log, LogLevel logLevel)
        {
            System = system;
            Log = log;
            LogLevel = logLevel;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        public void NotifyListeners(RemotingLifecycleEvent message)
        {
            System.EventStream.Publish(message);
            if (message.LogLevel() >= LogLevel) Log.Log(message.LogLevel(), message.ToString());
        }
    }
}

