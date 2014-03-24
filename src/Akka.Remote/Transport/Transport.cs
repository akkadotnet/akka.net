using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Google.ProtocolBuffers;

namespace Akka.Remote.Transport
{
    public abstract class Transport
    {
        protected Transport(ActorSystem system, Config config)
        {
            System = system;
            Config = config;
        }

        public Config Config { get; private set; }

        public ActorSystem System { get; private set; }

        public string SchemeIdentifier { get; protected set; }
        public abstract Address Listen();

        public abstract bool IsResponsibleFor(Address remote);
    }

    /// <summary>
    /// Marker interface for events that the registered listener for a <see cref="AssociationHandle"/> might receive.
    /// </summary>
    public interface IHandleEvent{ }

    /// <summary>
    /// Message sent to the listener registered to an association (via the TaskCompletionSource returned by <see cref="AssociationHandle.ReadHandlerSource"/>)
    /// </summary>
    public sealed class InboundPayload : IHandleEvent
    {
        public InboundPayload(ByteString payload)
        {
            Payload = payload;
        }

        public ByteString Payload { get; private set; }

        public override string ToString()
        {
            return string.Format("InboundPayload(size = {0} bytes)", Payload.Length);
        }
    }

    public sealed class Disassociated : IHandleEvent
    {
        internal readonly DisassociateInfo Info;

        public Disassociated(DisassociateInfo info)
        {
            Info = info;
        }
    }

    /// <summary>
    /// Supertype of possible disassociation reasons
    /// </summary>
    public abstract class DisassociateInfo { }

    public class Unknown : DisassociateInfo { }
    public class Shutdown : DisassociateInfo { }
    public class Quarantined : DisassociateInfo { }

    /// <summary>
    /// An interface that needs to be implemented by a user of an <see cref="AssociationHandle"/>
    /// in order to listen to association events
    /// </summary>
    public interface IHandleEventListener
    {
        void Notify(IHandleEvent ev);
    }

    /// <summary>
    /// Converts an <see cref="ActorRef"/> instance into an <see cref="IHandleEventListener"/>, so <see cref="IHandleEvent"/> messages
    /// can be passed directly to the Actor.
    /// </summary>
    public sealed class ActorHandleEventListener : IHandleEventListener
    {
        public readonly ActorRef Actor;

        public ActorHandleEventListener(ActorRef actor)
        {
            Actor = actor;
        }

        public void Notify(IHandleEvent ev)
        {
            Actor.Tell(ev);
        }
    }


    /// <summary>
    /// Marker type for whenever new actors / endpoints are associated with this <see cref="ActorSystem"/> via remoting.
    /// </summary>
    public interface IAssociationEvent
    {
         
    }

    /// <summary>
    /// Message sent to <see cref="IAssociationEventListener"/> registered to a transport (via the TaskCompletionSource returned by <see cref="Transport.Listen"/>)
    /// when the inbound association request arrives.
    /// </summary>
    public sealed class InboundAssociation : IAssociationEvent
    {
        public InboundAssociation(AssociationHandle association)
        {
            Association = association;
        }

        public AssociationHandle Association { get; private set; }
    }

    /// <summary>
    /// Listener interface for any object that can handle <see cref="IAssociationEvent"/> messages.
    /// </summary>
    public interface IAssociationEventListener
    {
        void Notify(IAssociationEvent ev);
    }

    /// <summary>
    /// Converts an <see cref="ActorRef"/> instance into an <see cref="IAssociationEventListener"/>, so <see cref="IAssociationEvent"/> messages
    /// can be passed directly to the Actor.
    /// </summary>
    public sealed class ActorAssociationEventListener : IAssociationEventListener
    {
        public ActorAssociationEventListener(ActorRef actor)
        {
            Actor = actor;
        }

        public ActorRef Actor { get; private set; }

        public void Notify(IAssociationEvent ev)
        {
            Actor.Tell(ev);
        }
    }

    public sealed class AssociationHandle
    {
        /// <summary>
        /// Address of the local endpoint
        /// </summary>
        public Address LocalAddress { get; protected set; }

        /// <summary>
        /// Address of the remote endpoint
        /// </summary>
        public Address RemoteAddress { get; protected set; }

        public TaskCompletionSource<IHandleEventListener> ReadHandlerSource { get; protected set; }
    }
}