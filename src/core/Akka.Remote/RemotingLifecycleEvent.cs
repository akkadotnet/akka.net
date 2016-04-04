//-----------------------------------------------------------------------
// <copyright file="RemotingLifecycleEvent.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

    public abstract class AssociationEvent : RemotingLifecycleEvent
    {
        public abstract Address LocalAddress { get; protected set; }
        public abstract Address RemoteAddress { get; protected set; }

        public abstract bool IsInbound { get; protected set; }

        protected string EventName;

        public override string ToString()
        {
            var networkDirection = IsInbound ? "<-" : "->";
            return string.Format("{0} [{1}] {2} {3}", EventName, LocalAddress, networkDirection, RemoteAddress);
        }
    }

    public sealed class AssociatedEvent : AssociationEvent
    {
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.DebugLevel;
        }

        public override Address LocalAddress { get; protected set; }
        public override Address RemoteAddress { get; protected set; }
        public override bool IsInbound { get; protected set; }

        public AssociatedEvent(Address localAddress, Address remoteAddress, bool inbound)
        {
            LocalAddress = localAddress;
            RemoteAddress = remoteAddress;
            IsInbound = inbound;
            EventName = "Associated";
        }
    }

    public sealed class DisassociatedEvent : AssociationEvent
    {
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.DebugLevel;
        }

        public override Address LocalAddress { get; protected set; }
        public override Address RemoteAddress { get; protected set; }
        public override bool IsInbound { get; protected set; }

        public DisassociatedEvent(Address localAddress, Address remoteAddress, bool inbound)
        {
            LocalAddress = localAddress;
            RemoteAddress = remoteAddress;
            IsInbound = inbound;
            EventName = "Disassociated";
        }
    }

    public sealed class AssociationErrorEvent : AssociationEvent
    {
        public AssociationErrorEvent(Exception cause, Address localAddress, Address remoteAddress, bool inbound, LogLevel level)
        {
            LocalAddress = localAddress;
            RemoteAddress = remoteAddress;
            IsInbound = inbound;
            EventName = "AssociationError";
            _level = level;
            Cause = cause;
        }

        public Exception Cause { get; private set; }

        private readonly LogLevel _level;
        public override LogLevel LogLevel()
        {
            return _level;
        }

        public override Address LocalAddress { get; protected set; }
        public override Address RemoteAddress { get; protected set; }
        public override bool IsInbound { get; protected set; }

        public override string ToString()
        {
            return string.Format("{0}: Error [{1}] [{2}]", base.ToString(), Cause.Message, Cause.StackTrace);
        }
    }

    public sealed class RemotingListenEvent : RemotingLifecycleEvent
    {
        public RemotingListenEvent(IList<Address> listenAddresses)
        {
            ListenAddresses = listenAddresses;
        }

        public IList<Address> ListenAddresses { get; private set; }

        public override LogLevel LogLevel()
        {
            return Event.LogLevel.InfoLevel;
        }

        public override string ToString()
        {
            return string.Format("Remoting now listens on addresses: [{0}]",
                ListenAddresses.Select(x => x.ToString()).Join(","));
        }
    }

    public sealed class RemotingShutdownEvent : RemotingLifecycleEvent
    {
        public override LogLevel LogLevel()
        {
            return Event.LogLevel.InfoLevel;
        }

        public override string ToString()
        {
            return "Remoting shut down";
        }
    }

    public sealed class RemotingErrorEvent : RemotingLifecycleEvent
    {
        public RemotingErrorEvent(Exception cause)
        {
            Cause = cause;
        }

        public Exception Cause { get; private set; }

        public override LogLevel LogLevel()
        {
            return Event.LogLevel.ErrorLevel;
        }

        public override string ToString()
        {
            return string.Format("Remoting error: [{0}] [{1}]", Cause.Message, Cause.StackTrace);
        }
    }

    public sealed class QuarantinedEvent : RemotingLifecycleEvent
    {
        public QuarantinedEvent(Address address, int uid)
        {
            Uid = uid;
            Address = address;
        }

        public Address Address { get; private set; }

        public int Uid { get; private set; }

        public override LogLevel LogLevel()
        {
            return Event.LogLevel.WarningLevel;
        }

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
    /// INTERNAL API.
    /// 
    /// Used for publishing remote lifecycle events to the <see cref="EventStream"/> of the provided <see cref="ActorSystem"/>.
    /// </summary>
    internal class EventPublisher
    {
        public ActorSystem System { get; private set; }

        public ILoggingAdapter Log { get; private set; }

        public readonly LogLevel LogLevel;

        public EventPublisher(ActorSystem system, ILoggingAdapter log, LogLevel logLevel)
        {
            System = system;
            Log = log;
            LogLevel = logLevel;
        }

        public void NotifyListeners(RemotingLifecycleEvent message)
        {
            System.EventStream.Publish(message);
            if (message.LogLevel() >= LogLevel) Log.Log(message.LogLevel(), message.ToString());
        }
    }
}

