//-----------------------------------------------------------------------
// <copyright file="RemotingLifecycleEvent.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// Base event data shared by remoting association lifecycle events.
    /// </summary>
    public abstract class AssociationEvent : RemotingLifecycleEvent
    {
        /// <summary>
        /// Gets the local transport address involved in the association.
        /// </summary>
        public abstract Address LocalAddress { get; protected set; }
        /// <summary>
        /// Gets the remote transport address involved in the association.
        /// </summary>
        public abstract Address RemoteAddress { get; protected set; }

        /// <summary>
        /// Gets whether the association was initiated by an inbound connection.
        /// </summary>
        public abstract bool IsInbound { get; protected set; }

        /// <summary>
        /// Event name included in the string representation of this association event.
        /// </summary>
        protected string EventName;

        /// <summary>
        /// Returns a string identifying the event and the direction between its local and remote addresses.
        /// </summary>
        /// <returns>The event name, local address, direction, and remote address.</returns>
        public override string ToString()
        {
            var networkDirection = IsInbound ? "<-" : "->";
            return string.Format("{0} [{1}] {2} {3}", EventName, LocalAddress, networkDirection, RemoteAddress);
        }
    }

    /// <summary>
    /// Event published when a transport association is established.
    /// </summary>
    public sealed class AssociatedEvent : AssociationEvent
    {
        /// <summary>
        /// Gets the log level used for an association-established event.
        /// </summary>
        /// <returns>The debug log level.</returns>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.DebugLevel;
        }

        /// <summary>
        /// Gets the local address of the established association.
        /// </summary>
        public override Address LocalAddress { get; protected set; }
        /// <summary>
        /// Gets the remote address of the established association.
        /// </summary>
        public override Address RemoteAddress { get; protected set; }
        /// <summary>
        /// Gets whether this side accepted the inbound connection.
        /// </summary>
        public override bool IsInbound { get; protected set; }

        /// <summary>
        /// Creates an event describing an established association.
        /// </summary>
        /// <param name="localAddress">The local transport address.</param>
        /// <param name="remoteAddress">The remote transport address.</param>
        /// <param name="inbound"><c>true</c> if the local transport accepted an inbound connection; otherwise, <c>false</c>.</param>
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
    /// Event published when an association fails with an error.
    /// </summary>
    public sealed class AssociationErrorEvent : AssociationEvent
    {
        /// <summary>
        /// Creates an event describing an association error.
        /// </summary>
        /// <param name="cause">The exception that caused the association error.</param>
        /// <param name="localAddress">The local transport address.</param>
        /// <param name="remoteAddress">The remote transport address.</param>
        /// <param name="inbound"><c>true</c> if this side accepted an inbound connection; otherwise, <c>false</c>.</param>
        /// <param name="level">The log level assigned to this event.</param>
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
        /// Gets the exception that caused the association error.
        /// </summary>
        public Exception Cause { get; private set; }

        private readonly LogLevel _level;
        /// <summary>
        /// Gets the log level assigned to this event.
        /// </summary>
		/// <returns>The configured event log level.</returns>
        public override LogLevel LogLevel()
        {
            return _level;
        }

        /// <summary>
        /// Gets the local address involved in the failed association.
        /// </summary>
        public override Address LocalAddress { get; protected set; }
        /// <summary>
        /// Gets the remote address involved in the failed association.
        /// </summary>
        public override Address RemoteAddress { get; protected set; }
        /// <summary>
        /// Gets whether the failed association was inbound.
        /// </summary>
        public override bool IsInbound { get; protected set; }

        /// <summary>
        /// Returns the association details and the cause's message and stack trace.
        /// </summary>
        /// <returns>A string describing the association error.</returns>
        public override string ToString()
        {
            return string.Format("{0}: Error [{1}] [{2}]", base.ToString(), Cause.Message, Cause.StackTrace);
        }
    }

    /// <summary>
    /// Event published when remoting begins listening on transport addresses.
    /// </summary>
    public sealed class RemotingListenEvent : RemotingLifecycleEvent
    {
        /// <summary>
        /// Creates an event describing the addresses on which remoting is listening.
        /// </summary>
        /// <param name="listenAddresses">The addresses bound by remoting transports.</param>
        public RemotingListenEvent(IList<Address> listenAddresses)
        {
            ListenAddresses = listenAddresses;
        }

        /// <summary>
        /// Gets the addresses on which remoting is listening.
        /// </summary>
        public IList<Address> ListenAddresses { get; private set; }

        /// <summary>
        /// Gets the log level used for a remoting-listen event.
        /// </summary>
        /// <returns>The info log level.</returns>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.InfoLevel;
        }

        /// <summary>
        /// Returns the addresses on which remoting is listening.
        /// </summary>
        /// <returns>A string listing the listening addresses.</returns>
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

       
        public override string ToString()
        {
            return "Remoting shut down";
        }
    }

    /// <summary>
    /// Event published when the remoting system encounters an error.
    /// </summary>
    public sealed class RemotingErrorEvent : RemotingLifecycleEvent
    {
        /// <summary>
        /// Creates an event for a remoting error.
        /// </summary>
        /// <param name="cause">The exception that caused the remoting error.</param>
        public RemotingErrorEvent(Exception cause)
        {
            Cause = cause;
        }

        /// <summary>
        /// Gets the exception that caused the remoting error.
        /// </summary>
        public Exception Cause { get; private set; }

        /// <summary>
        /// Gets the log level used for a remoting error event.
        /// </summary>
        /// <returns>The error log level.</returns>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.ErrorLevel;
        }

        /// <summary>
        /// Returns the error message and stack trace.
        /// </summary>
        /// <returns>A string describing the remoting error.</returns>
        public override string ToString()
        {
            return string.Format("Remoting error: [{0}] [{1}]", Cause.Message, Cause.StackTrace);
        }
    }

    /// <summary>
    /// Event published when an association to a remote system UID is quarantined.
    /// </summary>
    public sealed class QuarantinedEvent : RemotingLifecycleEvent
    {
        /// <summary>
        /// Creates an event describing a quarantined remote system.
        /// </summary>
        /// <param name="address">The address of the quarantined remote system.</param>
        /// <param name="uid">The UID of the quarantined remote system.</param>
        public QuarantinedEvent(Address address, long uid)
        {
            Uid = uid;
            Address = address;
        }

        /// <summary>
        /// Gets the address of the quarantined remote system.
        /// </summary>
        public Address Address { get; private set; }

        /// <summary>
        /// Gets the UID of the quarantined remote system.
        /// </summary>
        public long Uid { get; private set; }

        /// <summary>
        /// Gets the log level used for a quarantine event.
        /// </summary>
        /// <returns>The warning log level.</returns>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.WarningLevel;
        }

        /// <summary>
        /// Returns the quarantined address and UID with an explanation that messages to that UID are dead-lettered.
        /// </summary>
        /// <returns>A string describing the quarantine.</returns>
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
    /// Event published when this actor system is quarantined by a remote system.
    /// </summary>
    public sealed class ThisActorSystemQuarantinedEvent : RemotingLifecycleEvent
    {
        /// <summary>
        /// Creates an event describing this actor system being quarantined by a remote system.
        /// </summary>
        /// <param name="localAddress">The address of this actor system.</param>
        /// <param name="remoteAddress">The address of the remote system that quarantined it.</param>
        public ThisActorSystemQuarantinedEvent(Address localAddress, Address remoteAddress)
        {
            LocalAddress = localAddress;
            RemoteAddress = remoteAddress;
        }

        /// <summary>
        /// Gets the address of this quarantined actor system.
        /// </summary>
        public Address LocalAddress { get; private set; }

        /// <summary>
        /// Gets the address of the remote system that quarantined this system.
        /// </summary>
        public Address RemoteAddress { get; private set; }

        /// <summary>
        /// Gets the log level used when this system is quarantined by a remote system.
        /// </summary>
        /// <returns>The warning log level.</returns>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.WarningLevel;
        }

        /// <summary>
        /// Returns a message identifying the remote system and this quarantined system.
        /// </summary>
        /// <returns>A string describing the quarantine.</returns>
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
    internal sealed class EventPublisher
    {
        /// <summary>
        /// Gets the actor system whose event stream receives lifecycle events.
        /// </summary>
        public ActorSystem System { get; private set; }

        /// <summary>
        /// Gets the logger used to write lifecycle events.
        /// </summary>
        public ILoggingAdapter Log { get; private set; }

        /// <summary>
        /// Gets the minimum log level at which lifecycle events are written.
        /// </summary>
        public readonly LogLevel LogLevel;

        /// <summary>
        /// Creates a publisher for remoting lifecycle events.
        /// </summary>
        /// <param name="system">The actor system whose event stream publishes lifecycle events.</param>
        /// <param name="log">The logger used for lifecycle event messages.</param>
        /// <param name="logLevel">The minimum level at which events are logged.</param>
        public EventPublisher(ActorSystem system, ILoggingAdapter log, LogLevel logLevel)
        {
            System = system;
            Log = log;
            LogLevel = logLevel;
        }

        /// <summary>
        /// Publishes a lifecycle event and logs it when its level meets the configured threshold.
        /// </summary>
        /// <param name="message">The event to publish and, when enabled, log.</param>
        public void NotifyListeners(RemotingLifecycleEvent message)
        {
            System.EventStream.Publish(message);
            if (message.LogLevel() >= LogLevel) Log.Log(message.LogLevel(), message.ToString());
        }
    }
}

