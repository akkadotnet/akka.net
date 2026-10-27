//-----------------------------------------------------------------------
// <copyright file="Transport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// Abstract service provider interface for transports that create logical associations between remote endpoints.
    /// </summary>
    public abstract class Transport
    {
        /// <summary>
        /// Configuration used to initialize this transport, when the implementation exposes it.
        /// </summary>
        public Config Config { get; protected set; }

        /// <summary>
        /// Actor system that owns this transport.
        /// </summary>
        public ActorSystem System { get; protected set; }

        /// <summary>
        /// Scheme identifier used in this transport's addresses.
        /// </summary>
        public virtual string SchemeIdentifier { get; protected set; }
        /// <summary>
        /// Maximum payload size, in bytes, supported by this transport.
        /// </summary>
        public virtual long MaximumPayloadBytes { get; protected set; }
        /// <summary>
        /// Binds the transport and begins accepting inbound association requests.
        /// </summary>
        /// <returns>A task containing the bound address and a completion source for the inbound association listener.</returns>
        public abstract Task<(Address, TaskCompletionSource<IAssociationEventListener>)> Listen();

        /// <summary>
        /// Determines whether this transport can handle associations for the specified address.
        /// </summary>
        /// <param name="remote">Address of the remote transport endpoint.</param>
        /// <returns><see langword="true"/> if this transport is responsible for the address; otherwise <see langword="false"/>.</returns>
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

        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidAssociationException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected InvalidAssociationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
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
        /// Creates an event carrying a payload received from a remote association.
        /// </summary>
        /// <param name="payload">Payload bytes received from the remote endpoint.</param>
        public InboundPayload(ByteString payload)
        {
            Payload = payload;
        }

        /// <summary>
        /// Payload bytes received from the remote endpoint.
        /// </summary>
        public ByteString Payload { get; private set; }

        
        public override string ToString()
        {
            return $"InboundPayload(size = {Payload.Length} bytes)";
        }
    }

    /// <summary>
    /// Event indicating that an association has closed, with the reason reported by the transport.
    /// </summary>
    public sealed class Disassociated : IHandleEvent, IDeadLetterSuppression
    {
        /// <summary>
        /// Disassociation reason reported by the transport.
        /// </summary>
        internal readonly DisassociateInfo Info;

        /// <summary>
        /// Creates a disassociation event.
        /// </summary>
        /// <param name="info">Reason the association was disassociated.</param>
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
        /// Exception reported by the underlying transport.
        /// </summary>
        internal readonly Exception Cause;
        /// <summary>
        /// Descriptive message supplied by the underlying transport.
        /// </summary>
        internal readonly string Message;

        /// <summary>
        /// Creates an event describing a non-fatal underlying transport error.
        /// </summary>
        /// <param name="cause">Exception that caused the transport error.</param>
        /// <param name="message">Description of the transport error.</param>
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
        /// Disassociation occurred for an unspecified reason.
        /// </summary>
        Unknown = 0,
        /// <summary>
        /// The association is being closed because an endpoint is shutting down.
        /// </summary>
        Shutdown = 1,
        /// <summary>
        /// The remote endpoint has quarantined this system.
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
        /// Creates an inbound association event.
        /// </summary>
        /// <param name="association">Handle for the newly accepted association.</param>
        public InboundAssociation(AssociationHandle association)
        {
            Association = association;
        }

        /// <summary>
        /// Handle for the newly accepted association.
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

        
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((AssociationHandle) obj);
        }

        
        protected bool Equals(AssociationHandle other)
        {
            return Equals(LocalAddress, other.LocalAddress) && Equals(RemoteAddress, other.RemoteAddress);
        }

       
        public override int GetHashCode()
        {
            unchecked
            {
                return ((LocalAddress != null ? LocalAddress.GetHashCode() : 0) * 397) ^ (RemoteAddress != null ? RemoteAddress.GetHashCode() : 0);
            }
        }
    }
}

