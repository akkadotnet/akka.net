//-----------------------------------------------------------------------
// <copyright file="Transport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Google.Protobuf;
using System.Runtime.Serialization;
using Akka.Event;

namespace Akka.Remote.Transport
{
    /// <summary>
    /// TBD
    /// </summary>
    public abstract class Transport
    {
        /// <summary>
        /// TBD
        /// </summary>
        public Config Config { get; protected set; }

        /// <summary>
        /// TBD
        /// </summary>
        public ActorSystem System { get; protected set; }

        /// <summary>
        /// TBD
        /// </summary>
        public virtual string SchemeIdentifier { get; protected set; }
        /// <summary>
        /// TBD
        /// </summary>
        public virtual long MaximumPayloadBytes { get; protected set; }
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public abstract Task<(Address, TaskCompletionSource<IAssociationEventListener>)> Listen();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remote">TBD</param>
        /// <returns>TBD</returns>
        public abstract bool IsResponsibleFor(Address remote);

        /// <summary>
        /// Asynchronously opens a logical duplex link between two <see cref="Transport"/> entities over a network. It could be backed
        /// with a real transport layer connection (TCP), socketless connections provided over datagram protocols (UDP), and more.
        /// 
        /// This call returns a Task of an <see cref="AssociationHandle"/>. A faulted Task indicates that the association attempt was
        /// unsuccessful. If the exception is <see cref="InvalidAssociationException"/> then the association request was invalid and it's
        /// impossible to recover.
        /// </summary>
        /// <param name="remoteAddress">The address of the remote transport entity.</param>
        /// <returns>A status representing the failure or success containing an <see cref="AssociationHandle"/>.</returns>
        public abstract Task<AssociationHandle> Associate(Address remoteAddress);

        /// <summary>
        /// Shuts down the transport layer and releases all of the corresponding resources. Shutdown is asynchronous and is signaled
        /// by the result of the returned Task.
        /// 
        /// The transport SHOULD try flushing pending writes before becoming completely closed.
        /// </summary>
        /// <returns>Task signaling the completion of the shutdown.</returns>
        public abstract Task<bool> Shutdown();

        /// <summary>
        /// This method allows upper layers to send management commands to the transport. It is the responsibility of the sender to
        /// send appropriate commands to different transport implementations. Unknown commands will be ignored.
        /// </summary>
        /// <param name="message">Command message to send to the transport.</param>
        /// <returns>A Task that succeeds when the command was handled or dropped.</returns>
        public virtual Task<bool> ManagementCommand(object message)
        {
            return Task.Run(() => true);
        }
    }

    /// <summary>
    /// This exception is thrown when an association setup request is invalid and it is impossible to recover (malformed IP address, unknown hostname, etc...).
    /// </summary>
    public class InvalidAssociationException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidAssociationException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="cause">The exception that is the cause of the current exception.</param>
        public InvalidAssociationException(string message, Exception cause = null)
            : base(message, cause)
        {
        }

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidAssociationException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected InvalidAssociationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }

    /// <summary>
    /// Marker interface for events that the registered listener for a <see cref="AssociationHandle"/> might receive.
    /// </summary>
    public interface IHandleEvent : INoSerializationVerificationNeeded { }

    /// <summary>
    /// Message sent to the listener registered to an association (via the TaskCompletionSource returned by <see cref="AssociationHandle.ReadHandlerSource"/>)
    /// </summary>
    public sealed class InboundPayload : IHandleEvent
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="payload">TBD</param>
        public InboundPayload(ByteString payload)
        {
            Payload = payload;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public ByteString Payload { get; private set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"InboundPayload(size = {Payload.Length} bytes)";
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class Disassociated : IHandleEvent, IDeadLetterSuppression
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal readonly DisassociateInfo Info;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="info">TBD</param>
        public Disassociated(DisassociateInfo info)
        {
            Info = info;
        }
    }

    /// <summary>
    /// The underlying transport reported a non-fatal error
    /// </summary>
    public sealed class UnderlyingTransportError : IHandleEvent
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal readonly Exception Cause;
        /// <summary>
        /// TBD
        /// </summary>
        internal readonly string Message;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        /// <param name="message">TBD</param>
        public UnderlyingTransportError(Exception cause, string message)
        {
            Cause = cause;
            Message = message;
        }
    }

    /// <summary>
    /// Supertype of possible disassociation reasons
    /// </summary>
    public enum DisassociateInfo
    {
        /// <summary>
        /// TBD
        /// </summary>
        Unknown = 0,
        /// <summary>
        /// TBD
        /// </summary>
        Shutdown = 1,
        /// <summary>
        /// TBD
        /// </summary>
        Quarantined = 2
    }

    /// <summary>
    /// An interface that needs to be implemented by a user of an <see cref="AssociationHandle"/>
    /// in order to listen to association events
    /// </summary>
    public interface IHandleEventListener
    {
        /// <summary>
        /// Notify the listener about an <see cref="IHandleEvent"/>.
        /// </summary>
        /// <param name="ev">The <see cref="IHandleEvent"/> to notify the listener about</param>
        void Notify(IHandleEvent ev);
    }

    /// <summary>
    /// Converts an <see cref="IActorRef"/> into an <see cref="IHandleEventListener"/>, so <see cref="IHandleEvent"/> messages
    /// can be passed directly to the Actor.
    /// </summary>
    public sealed class ActorHandleEventListener : IHandleEventListener
    {
        /// <summary>
        /// The Actor to notify about <see cref="IHandleEvent"/> messages.
        /// </summary>
        public readonly IActorRef Actor;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorHandleEventListener"/> class.
        /// </summary>
        /// <param name="actor">The Actor to notify about <see cref="IHandleEvent"/> messages.</param>
        public ActorHandleEventListener(IActorRef actor)
        {
            Actor = actor;
        }

        /// <summary>
        /// Notify the Actor about an <see cref="IHandleEvent"/> message.
        /// </summary>
        /// <param name="ev">The <see cref="IHandleEvent"/> message to notify the Actor about</param>
        public void Notify(IHandleEvent ev)
        {
            Actor.Tell(ev);
        }
    }


    /// <summary>
    /// Marker type for whenever new actors / endpoints are associated with this <see cref="ActorSystem"/> via remoting.
    /// </summary>
    public interface IAssociationEvent : INoSerializationVerificationNeeded
    {

    }

    /// <summary>
    /// Message sent to <see cref="IAssociationEventListener"/> registered to a transport (via the TaskCompletionSource returned by <see cref="Transport.Listen"/>)
    /// when the inbound association request arrives.
    /// </summary>
    public sealed class InboundAssociation : IAssociationEvent
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="association">TBD</param>
        public InboundAssociation(AssociationHandle association)
        {
            Association = association;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public AssociationHandle Association { get; private set; }
    }

    /// <summary>
    /// Listener interface for any object that can handle <see cref="IAssociationEvent"/> messages.
    /// </summary>
    public interface IAssociationEventListener
    {
        /// <summary>
        /// Notify the listener about an <see cref="IAssociationEvent"/> message.
        /// </summary>
        /// <param name="ev">The <see cref="IAssociationEvent"/> message to notify the listener about</param>
        void Notify(IAssociationEvent ev);
    }

    /// <summary>
    /// Converts an <see cref="IActorRef"/> into an <see cref="IAssociationEventListener"/>, so <see cref="IAssociationEvent"/> messages
    /// can be passed directly to the Actor.
    /// </summary>
    public sealed class ActorAssociationEventListener : IAssociationEventListener
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActorAssociationEventListener"/> class.
        /// </summary>
        /// <param name="actor">The Actor to notify about <see cref="IAssociationEvent"/> messages.</param>
        public ActorAssociationEventListener(IActorRef actor)
        {
            Actor = actor;
        }

        /// <summary>
        /// The Actor to notify about <see cref="IAssociationEvent"/> messages.
        /// </summary>
        public IActorRef Actor { get; private set; }

        /// <summary>
        /// Notify the Actor about an <see cref="IAssociationEvent"/>.
        /// </summary>
        /// <param name="ev">The <see cref="IAssociationEvent"/> message to notify the Actor about</param>
        public void Notify(IAssociationEvent ev)
        {
            Actor.Tell(ev);
        }
    }

    /// <summary>
    /// A Service Provider Interface (SPI) layer for abstracting over logical links (associations) created by a <see cref="Transport"/>.
    /// Handles are responsible for providing an API for sending and receiving from the underlying channel.
    /// 
    /// To register a listener for processing incoming payload data, the listener must be registered by completing the Task returned by
    /// <see cref="AssociationHandle.ReadHandlerSource"/>. Incoming data is not processed until this registration takes place.
    /// </summary>
    public abstract class AssociationHandle
    {
        /// <summary>
        /// Creates a handle to an association between two remote addresses.
        /// </summary>
        /// <param name="localAddress">The local address to use.</param>
        /// <param name="remoteAddress">The remote address to use.</param>
        protected AssociationHandle(Address localAddress, Address remoteAddress)
        {
            LocalAddress = localAddress;
            RemoteAddress = remoteAddress;
            ReadHandlerSource = new TaskCompletionSource<IHandleEventListener>();
        }

        /// <summary>
        /// Address of the local endpoint
        /// </summary>
        public Address LocalAddress { get; protected set; }

        /// <summary>
        /// Address of the remote endpoint
        /// </summary>
        public Address RemoteAddress { get; protected set; }

        /// <summary>
        /// The TaskCompletionSource returned by this call must be completed with an <see cref="IHandleEventListener"/> to
        /// register a listener responsible for handling the incoming payload. Until the listener is not registered the
        /// transport SHOULD buffer incoming messages.
        /// </summary>
        public TaskCompletionSource<IHandleEventListener> ReadHandlerSource { get; protected set; }

        /// <summary>
        /// Asynchronously sends the specified <paramref name="payload"/> to the remote endpoint. This method's implementation MUST be thread-safe
        /// as it might be called from different threads. This method MUST NOT block.
        /// 
        /// Writes guarantee ordering of messages, but not their reception. The call to write returns with a boolean indicating if the
        /// channel was ready for writes or not. A return value of false indicates that the channel is not yet ready for deliver 
        /// (e.g.: the write buffer is full)and the sender  needs to wait until the channel becomes ready again.
        /// 
        /// Returning false also means that the current write was dropped (this MUST be guaranteed to ensure duplication-free delivery).
        /// </summary>
        /// <param name="payload">The payload to be delivered to the remote endpoint.</param>
        /// <returns>
        /// Bool indicating the availability of the association for subsequent writes.
        /// </returns>
        public abstract bool Write(ByteString payload);

        /// <summary>
        /// Closes the underlying transport link, if needed. Some transports might not need an explicit teardown (UDP) and some
        /// transports may not support it. Remote endpoint of the channel or connection MAY be notified, but this is not
        /// guaranteed.
        /// 
        /// The transport that provides the handle MUST guarantee that <see cref="Disassociate()"/> could be called arbitrarily many times.
        /// </summary>
        [Obsolete("Use the method that states reasons to make sure disassociation reasons are logged.")]
        public abstract void Disassociate();

        /// <summary>
        /// Closes the underlying transport link, if needed. Some transports might not need an explicit teardown (UDP) and some
        /// transports may not support it. Remote endpoint of the channel or connection MAY be notified, but this is not
        /// guaranteed.
        /// 
        /// The transport that provides the handle MUST guarantee that <see cref="Disassociate()"/> could be called arbitrarily many times.
        /// </summary>
        public void Disassociate(string reason, ILoggingAdapter log)
        {
            if (log.IsDebugEnabled)
            {
                log.Debug("Association between local [{0}] and remote [{1}] was disassociated because {2}", LocalAddress, RemoteAddress, reason);
            }

#pragma warning disable 618
            Disassociate();
#pragma warning restore 618
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((AssociationHandle) obj);
        }

        /// <inheritdoc/>
        protected bool Equals(AssociationHandle other)
        {
            return Equals(LocalAddress, other.LocalAddress) && Equals(RemoteAddress, other.RemoteAddress);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((LocalAddress != null ? LocalAddress.GetHashCode() : 0) * 397) ^ (RemoteAddress != null ? RemoteAddress.GetHashCode() : 0);
            }
        }
    }
}

