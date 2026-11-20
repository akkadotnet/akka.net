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
    /// Base class for the remoting lifecycle events that remoting publishes to the <see cref="EventStream"/>
    /// when it starts listening, connects, disconnects, quarantines, fails, or shuts down.
    /// </summary>
    public abstract class RemotingLifecycleEvent
    {
        /// <summary>
        /// The level at which remoting logs this event.
        /// </summary>
        /// <returns>The <see cref="Event.LogLevel"/> for this event.</returns>
        public abstract LogLevel LogLevel();
    }

    /// <summary>
    /// Base class for events about an association (a connection) between this actor system and a remote one.
    /// </summary>
    public abstract class AssociationEvent : RemotingLifecycleEvent
    {
        /// <summary>
        /// The address of this actor system.
        /// </summary>
        public abstract Address LocalAddress { get; protected set; }

        /// <summary>
        /// The address of the remote actor system.
        /// </summary>
        public abstract Address RemoteAddress { get; protected set; }

        /// <summary>
        /// <c>true</c> if the remote system opened the association, <c>false</c> if this system did.
        /// </summary>
        public abstract bool IsInbound { get; protected set; }

        /// <summary>
        /// The name of the event, used by <see cref="ToString"/>.
        /// </summary>
        protected string EventName;

        /// <inheritdoc/>
        public override string ToString()
        {
            var networkDirection = IsInbound ? "<-" : "->";
            return string.Format("{0} [{1}] {2} {3}", EventName, LocalAddress, networkDirection, RemoteAddress);
        }
    }

    /// <summary>
    /// Event that is published when an association to another <see cref="ActorSystem"/> is established.
    /// </summary>
    public sealed class AssociatedEvent : AssociationEvent
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
        /// Creates a new <see cref="AssociatedEvent"/> instance.
        /// </summary>
        /// <param name="localAddress">The address of the current actor system.</param>
        /// <param name="remoteAddress">The address of the remote actor system.</param>
        /// <param name="inbound"><c>true</c> if the remote system opened the association, <c>false</c> if this system did.</param>
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
        /// <param name="inbound"><c>true</c> if this side of the connection was inbound, <c>false</c> if it was outbound.</param>
        public DisassociatedEvent(Address localAddress, Address remoteAddress, bool inbound)
        {
            LocalAddress = localAddress;
            RemoteAddress = remoteAddress;
            IsInbound = inbound;
            EventName = "Disassociated";
        }
    }

    /// <summary>
    /// Event that is published when an association to another <see cref="ActorSystem"/> fails.
    /// </summary>
    public sealed class AssociationErrorEvent : AssociationEvent
    {
        /// <summary>
        /// Creates a new <see cref="AssociationErrorEvent"/> instance.
        /// </summary>
        /// <param name="cause">The error that made the association fail.</param>
        /// <param name="localAddress">The address of the current actor system.</param>
        /// <param name="remoteAddress">The address of the remote actor system.</param>
        /// <param name="inbound"><c>true</c> if the remote system opened the association, <c>false</c> if this system did.</param>
        /// <param name="level">The level at which to log this event.</param>
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
        /// The error that made the association fail.
        /// </summary>
        public Exception Cause { get; private set; }

        private readonly LogLevel _level;

        /// <inheritdoc/>
        public override LogLevel LogLevel()
        {
            return _level;
        }

        /// <inheritdoc/>
        public override Address LocalAddress { get; protected set; }

        /// <inheritdoc/>
        public override Address RemoteAddress { get; protected set; }

        /// <inheritdoc/>
        public override bool IsInbound { get; protected set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format("{0}: Error [{1}] [{2}]", base.ToString(), Cause.Message, Cause.StackTrace);
        }
    }

    /// <summary>
    /// Event that is published when remoting starts listening for inbound connections.
    /// </summary>
    public sealed class RemotingListenEvent : RemotingLifecycleEvent
    {
        /// <summary>
        /// Creates a new <see cref="RemotingListenEvent"/> instance.
        /// </summary>
        /// <param name="listenAddresses">The addresses remoting listens on.</param>
        public RemotingListenEvent(IList<Address> listenAddresses)
        {
            ListenAddresses = listenAddresses;
        }

        /// <summary>
        /// The addresses remoting listens on, one per transport.
        /// </summary>
        public IList<Address> ListenAddresses { get; private set; }

        /// <inheritdoc/>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.InfoLevel;
        }

        /// <inheritdoc/>
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
    /// Event that is published when remoting fails to start or fails to shut down cleanly.
    /// </summary>
    public sealed class RemotingErrorEvent : RemotingLifecycleEvent
    {
        /// <summary>
        /// Creates a new <see cref="RemotingErrorEvent"/> instance.
        /// </summary>
        /// <param name="cause">The error.</param>
        public RemotingErrorEvent(Exception cause)
        {
            Cause = cause;
        }

        /// <summary>
        /// The error.
        /// </summary>
        public Exception Cause { get; private set; }

        /// <inheritdoc/>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.ErrorLevel;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format("Remoting error: [{0}] [{1}]", Cause.Message, Cause.StackTrace);
        }
    }

    /// <summary>
    /// Event that is published when this actor system quarantines a remote actor system.
    /// All further messages to that incarnation (UID) of the remote system go to dead letters,
    /// and it must restart to talk to this system again.
    /// </summary>
    public sealed class QuarantinedEvent : RemotingLifecycleEvent
    {
        /// <summary>
        /// Creates a new <see cref="QuarantinedEvent"/> instance.
        /// </summary>
        /// <param name="address">The address of the quarantined remote system.</param>
        /// <param name="uid">The UID of the quarantined remote system.</param>
        public QuarantinedEvent(Address address, int uid)
        {
            Uid = uid;
            Address = address;
        }

        /// <summary>
        /// The address of the quarantined remote system.
        /// </summary>
        public Address Address { get; private set; }

        /// <summary>
        /// The UID of the quarantined remote system.
        /// </summary>
        public int Uid { get; private set; }

        /// <inheritdoc/>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.WarningLevel;
        }

        /// <inheritdoc/>
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
    /// Event that is published when a remote actor system has quarantined this actor system and refuses
    /// to talk to it. This system must restart to talk to that remote system again.
    /// </summary>
    public sealed class ThisActorSystemQuarantinedEvent : RemotingLifecycleEvent
    {
        /// <summary>
        /// Creates a new <see cref="ThisActorSystemQuarantinedEvent"/> without a remote UID.
        /// </summary>
        /// <param name="localAddress">The address of this actor system.</param>
        /// <param name="remoteAddress">The address of the remote system that quarantined this system.</param>
        public ThisActorSystemQuarantinedEvent(Address localAddress, Address remoteAddress)
            : this(localAddress, remoteAddress, null)
        {
        }

        /// <summary>
        /// Creates a new <see cref="ThisActorSystemQuarantinedEvent"/>.
        /// </summary>
        /// <param name="localAddress">The address of this actor system.</param>
        /// <param name="remoteAddress">The address of the remote system that quarantined this system.</param>
        /// <param name="remoteUid">The UID of the remote system that quarantined this system, when known.</param>
        public ThisActorSystemQuarantinedEvent(Address localAddress, Address remoteAddress, int? remoteUid)
        {
            LocalAddress = localAddress;
            RemoteAddress = remoteAddress;
            RemoteUid = remoteUid;
        }

        /// <summary>
        /// The address of this actor system.
        /// </summary>
        public Address LocalAddress { get; private set; }

        /// <summary>
        /// The address of the remote system that quarantined this system.
        /// </summary>
        public Address RemoteAddress { get; private set; }

        /// <summary>
        /// The UID of the remote system that quarantined this system, or <c>null</c> if it isn't known.
        /// Use it to tell which incarnation of the remote system quarantined this one.
        /// </summary>
        public int? RemoteUid { get; private set; }

        /// <inheritdoc/>
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.WarningLevel;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return RemoteUid.HasValue
                ? string.Format("The remote system {0} (UID [{1}]) has quarantined this system {2}.", RemoteAddress, RemoteUid.Value, LocalAddress)
                : string.Format("The remote system {0} has quarantined this system {1}.", RemoteAddress, LocalAddress);
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
        /// The actor system whose <see cref="EventStream"/> receives the events.
        /// </summary>
        public ActorSystem System { get; private set; }

        /// <summary>
        /// The logger used to log published events.
        /// </summary>
        public ILoggingAdapter Log { get; private set; }

        /// <summary>
        /// Events at or above this level are also logged.
        /// </summary>
        public readonly LogLevel LogLevel;

        /// <summary>
        /// Creates a new <see cref="EventPublisher"/>.
        /// </summary>
        /// <param name="system">The actor system whose <see cref="EventStream"/> receives the events.</param>
        /// <param name="log">The logger used to log published events.</param>
        /// <param name="logLevel">Events at or above this level are also logged.</param>
        public EventPublisher(ActorSystem system, ILoggingAdapter log, LogLevel logLevel)
        {
            System = system;
            Log = log;
            LogLevel = logLevel;
        }

        /// <summary>
        /// Publishes the event to the <see cref="EventStream"/>, and logs it if its level is at or above <see cref="LogLevel"/>.
        /// </summary>
        /// <param name="message">The event to publish.</param>
        public void NotifyListeners(RemotingLifecycleEvent message)
        {
            System.EventStream.Publish(message);
            if (message.LogLevel() >= LogLevel) Log.Log(message.LogLevel(), message.ToString());
        }
    }
}
