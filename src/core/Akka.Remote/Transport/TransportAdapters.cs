//-----------------------------------------------------------------------
// <copyright file="TransportAdapters.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Event;


namespace Akka.Remote.Transport
{
    /// <summary>
    /// Interface for producing adapters that can wrap an underlying transport and augment it with additional behavior.
    /// </summary>
    public interface ITransportAdapterProvider
    {
        /// <summary>
        /// Create a transport adapter that wraps the underlying transport
        /// </summary>
        /// <param name="wrappedTransport">The transport that will be wrapped.</param>
        /// <param name="system">The actor system to which this transport belongs.</param>
        /// <returns>A transport wrapped with the new adapter.</returns>
        Transport Create(Transport wrappedTransport, ExtendedActorSystem system);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class TransportAdaptersExtension : ExtensionIdProvider<TransportAdapters>
    {
        /// <inheritdoc cref="ExtensionIdProvider{T}"/>
        public override TransportAdapters CreateExtension(ExtendedActorSystem system)
        {
            return new TransportAdapters((ActorSystemImpl)system);
        }

        #region Static methods

        /// <summary>
        /// Gets the transport adapter registry extension for the supplied actor system.
        /// </summary>
        /// <param name="system">Actor system whose adapter registry should be retrieved.</param>
        /// <returns>The transport adapter registry extension.</returns>
        public static TransportAdapters For(ActorSystem system)
        {
            return system.WithExtension<TransportAdapters, TransportAdaptersExtension>();
        }

        #endregion
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Extension that allows us to look up transport adapters based upon the settings provided inside <see cref="RemoteSettings"/>
    /// </summary>
    internal sealed class TransportAdapters : IExtension
    {
        /// <summary>
        /// Initializes the adapter registry from the remote settings of the actor system.
        /// </summary>
        /// <param name="system">Actor system whose configured transport adapters are registered.</param>
        public TransportAdapters(ExtendedActorSystem system)
        {
            System = system;
            Settings = ((IRemoteActorRefProvider)system.Provider).RemoteSettings;
        }

        /// <summary>
        /// The ActorSystem
        /// </summary>
        public ActorSystem System { get; private set; }

        /// <summary>
        /// The Akka.Remote settings
        /// </summary>
        private readonly RemoteSettings Settings;

        private Dictionary<string, ITransportAdapterProvider> _adaptersTable;

        private Dictionary<string, ITransportAdapterProvider> AdaptersTable()
        {
            if (_adaptersTable != null) return _adaptersTable;
            _adaptersTable = new Dictionary<string, ITransportAdapterProvider>();
            foreach (var adapter in Settings.Adapters)
            {
                try
                {
                    var adapterTypeName = Type.GetType(adapter.Value);
                    if (adapterTypeName is null)
                        throw new ArgumentException(
                            $"Cannot initiate transport adapter {adapter.Value}. Type could not be resolved.");
                    var newAdapter = (ITransportAdapterProvider)Activator.CreateInstance(adapterTypeName);
                    _adaptersTable.Add(adapter.Key, newAdapter);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Cannot initiate transport adapter {adapter.Value}", ex);
                }
            }

            return _adaptersTable;
        }

        /// <summary>
        /// Returns the configured provider for an adapter name.
        /// </summary>
        /// <param name="name">Name used to register the transport adapter in remote settings.</param>
        /// <exception cref="ArgumentException">Thrown when no provider is registered under <paramref name="name"/>.</exception>
        /// <returns>The provider configured for the adapter name.</returns>
        public ITransportAdapterProvider GetAdapterProvider(string name)
        {
            if (AdaptersTable().TryGetValue(name, out var provider))
                return provider;

            throw new ArgumentException($"There is no registered transport adapter provider with name {name}");
        }
    }

    /// <summary>
    /// Used to augment the protocol scheme of transports when enabled.
    /// </summary>
    public class SchemeAugmenter
    {
        /// <summary>
        /// Creates a new <see cref="SchemeAugmenter"/> instance.
        /// </summary>
        /// <param name="addedSchemeIdentifier">The new identifier that will be added to the front of the pipeline.</param>
        public SchemeAugmenter(string addedSchemeIdentifier)
        {
            AddedSchemeIdentifier = addedSchemeIdentifier;
        }

        /// <summary>
        /// The scheme that will be added to the front of the protocol.
        /// I.E. if using a TLS augmentor, the this field might read "ssl"
        /// and the full scheme of addresses generated using this transport
        /// might read "akka.tcp.ssl", the latter part being added by this augmenter.
        /// </summary>
        public readonly string AddedSchemeIdentifier;

        /// <summary>
        /// Prefixes a transport protocol scheme with this adapter's scheme identifier.
        /// </summary>
        /// <param name="originalScheme">Underlying transport protocol scheme.</param>
        /// <returns>The scheme identifier followed by the underlying protocol scheme.</returns>
        public string AugmentScheme(string originalScheme)
        {
            return string.Format("{0}.{1}", AddedSchemeIdentifier, originalScheme);
        }

        /// <summary>
        /// Returns an address with this adapter's scheme identifier prepended to its protocol.
        /// </summary>
        /// <param name="address">Address whose protocol should be augmented.</param>
        /// <returns>A copy of the address with the augmented protocol.</returns>
        public Address AugmentScheme(Address address)
        {
            var protocol = AugmentScheme(address.Protocol);
            return address.WithProtocol(protocol);
        }

        /// <summary>
        /// Removes this adapter's scheme prefix when it appears at the start of a protocol scheme.
        /// </summary>
        /// <param name="scheme">Protocol scheme from which to remove the adapter prefix.</param>
        /// <returns>The scheme without this adapter's prefix, or the original scheme if the prefix is absent.</returns>
        public string RemoveScheme(string scheme)
        {
            if (scheme.StartsWith(string.Format("{0}.", AddedSchemeIdentifier)))
                return scheme.Remove(0, AddedSchemeIdentifier.Length + 1);
            return scheme;
        }

        /// <summary>
        /// Returns an address with this adapter's scheme prefix removed from its protocol when present.
        /// </summary>
        /// <param name="address">Address whose protocol may contain this adapter's prefix.</param>
        /// <returns>A copy of the address with the adapter prefix removed when present.</returns>
        public Address RemoveScheme(Address address)
        {
            var protocol = RemoveScheme(address.Protocol);
            return address.WithProtocol(protocol);
        }
    }

    /// <summary>
    /// An adapter that wraps a transport and provides interception capabilities
    /// </summary>
    public abstract class AbstractTransportAdapter : Transport
    {
        /// <summary>
        /// Creates an adapter that wraps the specified transport.
        /// </summary>
        /// <param name="wrappedTransport">Underlying transport intercepted by this adapter.</param>
        protected AbstractTransportAdapter(Transport wrappedTransport)
        {
            WrappedTransport = wrappedTransport;
        }

        /// <summary>
        /// Underlying transport that this adapter wraps.
        /// </summary>
        protected Transport WrappedTransport;

        /// <summary>
        /// Scheme prefix applied to the wrapped transport's protocol identifier and addresses.
        /// </summary>
        protected abstract SchemeAugmenter SchemeAugmenter { get; }

        /// <summary>
        /// Protocol scheme identifier of the wrapped transport with this adapter's prefix applied.
        /// </summary>
        public override string SchemeIdentifier
        {
            get
            {
                return SchemeAugmenter.AugmentScheme(WrappedTransport.SchemeIdentifier);
            }
        }

        /// <summary>
        /// Maximum payload size supported by the wrapped transport.
        /// </summary>
        public override long MaximumPayloadBytes
        {
            get
            {
                return WrappedTransport.MaximumPayloadBytes;
            }
        }

        /// <summary>
        /// Intercepts the wrapped transport's listener and returns the listener to register with it.
        /// </summary>
        /// <param name="listenAddress">Address bound by the wrapped transport.</param>
        /// <param name="listenerTask">Task that completes with the upstream listener for inbound associations.</param>
        /// <returns>Task that completes with the listener the wrapped transport should notify.</returns>
        protected abstract Task<IAssociationEventListener> InterceptListen(Address listenAddress,
            Task<IAssociationEventListener> listenerTask);

        /// <summary>
        /// Intercepts a request to associate with a remote address stripped of this adapter's scheme.
        /// </summary>
        /// <param name="remoteAddress">Remote address with this adapter's scheme removed.</param>
        /// <param name="statusPromise">Completion source for the adapted association handle.</param>
        protected abstract void InterceptAssociate(Address remoteAddress,
            TaskCompletionSource<AssociationHandle> statusPromise);

        /// <summary>
        /// Delegates responsibility checks to the wrapped transport.
        /// </summary>
        /// <param name="remote">Address to check.</param>
        /// <returns>Whether the wrapped transport is responsible for the address.</returns>
        public override bool IsResponsibleFor(Address remote)
        {
            return WrappedTransport.IsResponsibleFor(remote);
        }

        /// <summary>
        /// Starts listening on the wrapped transport and intercepts inbound association events.
        /// </summary>
        /// <returns>A task containing the address with the adapter scheme applied and a listener completion source for inbound associations.</returns>
        public override Task<(Address, TaskCompletionSource<IAssociationEventListener>)> Listen()
        {
            var upstreamListenerPromise = new TaskCompletionSource<IAssociationEventListener>();
            return WrappedTransport.Listen().ContinueWith(async listenerTask =>
            {
                var listenAddress = listenerTask.Result.Item1;
                var listenerPromise = listenerTask.Result.Item2;
                listenerPromise.TrySetResult(await InterceptListen(listenAddress, upstreamListenerPromise.Task).ConfigureAwait(false));
                return (SchemeAugmenter.AugmentScheme(listenAddress), upstreamListenerPromise);
            }, TaskContinuationOptions.ExecuteSynchronously).Unwrap();
        }

        /// <summary>
        /// Requests an association after removing this adapter's scheme from the remote address.
        /// </summary>
        /// <param name="remoteAddress">Remote address exposed by the adapter.</param>
        /// <returns>A task completed by the adapter's association interception logic.</returns>
        public override Task<AssociationHandle> Associate(Address remoteAddress)
        {
            var statusPromise = new TaskCompletionSource<AssociationHandle>();
            InterceptAssociate(SchemeAugmenter.RemoveScheme(remoteAddress), statusPromise);
            return statusPromise.Task;
        }

        /// <summary>
        /// Shuts down the wrapped transport.
        /// </summary>
        /// <returns>A task that completes with the wrapped transport's shutdown result.</returns>
        public override Task<bool> Shutdown()
        {
            return WrappedTransport.Shutdown();
        }
    }

    /// <summary>
    /// Base association handle that wraps another handle and exposes addresses with an added transport scheme.
    /// </summary>
    public abstract class AbstractTransportAdapterHandle : AssociationHandle
    {
        /// <summary>
        /// Wraps an association handle and adds the specified scheme identifier to its endpoint addresses.
        /// </summary>
        /// <param name="wrappedHandle">Underlying association handle to wrap.</param>
        /// <param name="addedSchemeIdentifier">Scheme identifier to add to the local and remote addresses.</param>
        protected AbstractTransportAdapterHandle(AssociationHandle wrappedHandle, string addedSchemeIdentifier)
            : this(wrappedHandle.LocalAddress, wrappedHandle.RemoteAddress, wrappedHandle, addedSchemeIdentifier) { }

        /// <summary>
        /// Wraps an association handle while supplying the original local and remote addresses explicitly.
        /// </summary>
        /// <param name="originalLocalAddress">Local address before adding the adapter's scheme identifier.</param>
        /// <param name="originalRemoteAddress">Remote address before adding the adapter's scheme identifier.</param>
        /// <param name="wrappedHandle">Underlying association handle to wrap.</param>
        /// <param name="addedSchemeIdentifier">Scheme identifier to add to both addresses.</param>
        protected AbstractTransportAdapterHandle(Address originalLocalAddress, Address originalRemoteAddress, AssociationHandle wrappedHandle, string addedSchemeIdentifier) : base(originalLocalAddress, originalRemoteAddress)
        {
            WrappedHandle = wrappedHandle;
            OriginalRemoteAddress = originalRemoteAddress;
            OriginalLocalAddress = originalLocalAddress;
            SchemeAugmenter = new SchemeAugmenter(addedSchemeIdentifier);
            RemoteAddress = SchemeAugmenter.AugmentScheme(OriginalRemoteAddress);
            LocalAddress = SchemeAugmenter.AugmentScheme(OriginalLocalAddress);
        }

        /// <summary>
        /// Local endpoint address before the adapter scheme was added.
        /// </summary>
        public Address OriginalLocalAddress { get; private set; }

        /// <summary>
        /// Remote endpoint address before the adapter scheme was added.
        /// </summary>
        public Address OriginalRemoteAddress { get; private set; }

        /// <summary>
        /// Underlying association handle wrapped by this handle.
        /// </summary>
        public AssociationHandle WrappedHandle { get; private set; }

        /// <summary>
        /// Helper that adds or removes this adapter's protocol scheme prefix.
        /// </summary>
        protected SchemeAugmenter SchemeAugmenter { get; private set; }

        /// <summary>
        /// Compares the original addresses and wrapped handle of two adapter handles.
        /// </summary>
        /// <param name="other">Adapter handle to compare with this handle.</param>
        /// <returns><see langword="true"/> when the original addresses and wrapped handles are equal; otherwise <see langword="false"/>.</returns>
        protected bool Equals(AbstractTransportAdapterHandle other)
        {
            return Equals(OriginalLocalAddress, other.OriginalLocalAddress) && Equals(OriginalRemoteAddress, other.OriginalRemoteAddress) && Equals(WrappedHandle, other.WrappedHandle);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((AbstractTransportAdapterHandle)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = base.GetHashCode() + (OriginalLocalAddress != null ? OriginalLocalAddress.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (OriginalRemoteAddress != null ? OriginalRemoteAddress.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (WrappedHandle != null ? WrappedHandle.GetHashCode() : 0);
                return hashCode;
            }
        }
    }

    /// <summary>
    /// Marker interface for all transport operations
    /// </summary>
    public abstract class TransportOperation : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Timeout used by transport adapter operations that use actor asks.
        /// </summary>
        public static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(5);
    }

    /// <summary>
    /// Manager message indicating that the upstream association event listener has registered.
    /// </summary>
    public sealed class ListenerRegistered : TransportOperation
    {
        /// <summary>
        /// Creates a listener-registration message.
        /// </summary>
        /// <param name="listener">Listener that should receive inbound association events.</param>
        public ListenerRegistered(IAssociationEventListener listener)
        {
            Listener = listener;
        }

        /// <summary>
        /// Upstream listener registered for inbound association events.
        /// </summary>
        public IAssociationEventListener Listener { get; private set; }
    }

    /// <summary>
    /// Manager message requesting an association from the wrapped transport.
    /// </summary>
    public sealed class AssociateUnderlying : TransportOperation
    {
        /// <summary>
        /// Creates an underlying-association request.
        /// </summary>
        /// <param name="remoteAddress">Remote address to associate with.</param>
        /// <param name="statusPromise">Completion source for the underlying association handle.</param>
        public AssociateUnderlying(Address remoteAddress, TaskCompletionSource<AssociationHandle> statusPromise)
        {
            RemoteAddress = remoteAddress;
            StatusPromise = statusPromise;
        }

        /// <summary>
        /// Remote address requested for association.
        /// </summary>
        public Address RemoteAddress { get; private set; }

        /// <summary>
        /// Completion source for the underlying association handle.
        /// </summary>
        public TaskCompletionSource<AssociationHandle> StatusPromise { get; private set; }
    }

    /// <summary>
    /// Manager message requesting the wrapped transport to listen at an address.
    /// </summary>
    public sealed class ListenUnderlying : TransportOperation
    {
        /// <summary>
        /// Creates a listen request for the wrapped transport manager.
        /// </summary>
        /// <param name="listenAddress">Address bound by the wrapped transport.</param>
        /// <param name="upstreamListener">Task that completes with the upstream listener for inbound associations.</param>
        public ListenUnderlying(Address listenAddress, Task<IAssociationEventListener> upstreamListener)
        {
            UpstreamListener = upstreamListener;
            ListenAddress = listenAddress;
        }

        /// <summary>
        /// Address bound by the wrapped transport.
        /// </summary>
        public Address ListenAddress { get; private set; }

        /// <summary>
        /// Task that completes with the upstream listener for inbound associations.
        /// </summary>
        public Task<IAssociationEventListener> UpstreamListener { get; private set; }
    }

    /// <summary>
    /// Manager message requesting that the underlying association be closed.
    /// </summary>
    public sealed class DisassociateUnderlying : TransportOperation, IDeadLetterSuppression
    {
        /// <summary>
        /// Creates a disassociation request with an optional reason.
        /// </summary>
        /// <param name="info">Reason for closing the association.</param>
        public DisassociateUnderlying(DisassociateInfo info = DisassociateInfo.Unknown)
        {
            Info = info;
        }

        /// <summary>
        /// Reason supplied when requesting the underlying association to close.
        /// </summary>
        public DisassociateInfo Info { get; private set; }
    }

    /// <summary>
    ///  Actor-based transport adapter
    /// </summary>
    public abstract class ActorTransportAdapter : AbstractTransportAdapter
    {
        /// <summary>
        /// Creates an actor-based adapter around an underlying transport.
        /// </summary>
        /// <param name="wrappedTransport">Transport whose operations are coordinated by a manager actor.</param>
        /// <param name="system">Actor system hosting the manager and association actors.</param>
        protected ActorTransportAdapter(Transport wrappedTransport, ActorSystem system) : base(wrappedTransport)
        {
            System = system;
        }

        /// <summary>
        /// Actor name used to register the transport adapter manager.
        /// </summary>
        protected abstract string ManagerName { get; }
        /// <summary>
        /// Props used to create the transport adapter manager actor.
        /// </summary>
        protected abstract Props ManagerProps { get; }


        /// <summary>
        /// Maximum duration to wait for manager actor operations that use asks.
        /// </summary>
        public static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Manager actor that coordinates listen, association, and shutdown operations.
        /// </summary>
        protected volatile IActorRef manager;

        private Task<IActorRef> RegisterManager()
        {
            return System.ActorSelection("/system/transports").Ask<IActorRef>(new RegisterTransportActor(ManagerProps, ManagerName));
        }

        /// <inheritdoc/>
        protected override Task<IAssociationEventListener> InterceptListen(Address listenAddress, Task<IAssociationEventListener> listenerTask)
        {
            return RegisterManager().ContinueWith(mgrTask =>
            {
                manager = mgrTask.Result;
                manager.Tell(new ListenUnderlying(listenAddress, listenerTask));
                return (IAssociationEventListener)new ActorAssociationEventListener(manager);
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <inheritdoc/>
        protected override void InterceptAssociate(Address remoteAddress, TaskCompletionSource<AssociationHandle> statusPromise)
        {
            manager.Tell(new AssociateUnderlying(remoteAddress, statusPromise));
        }

        /// <inheritdoc/>
        public override Task<bool> Shutdown()
        {
            var stopTask = manager.GracefulStop((RARP.For(System).Provider).RemoteSettings.FlushWait);

            // Sequence the wrapped-transport teardown AFTER the manager (and its child association
            // actors) have stopped. Those children write the graceful Disassociate PDU as part of
            // their own shutdown; running WrappedTransport.Shutdown() concurrently (as this used to)
            // let the underlying transport force-close the channel out from under a not-yet-flushed
            // Disassociate. Peers then kept a "zombie" association until the transport failure
            // detector tripped (up to acceptable-heartbeat-pause, 120s by default). The manager stop
            // is bounded by flush-wait-on-shutdown, and the wrapped transport is torn down regardless
            // of whether the manager stopped cleanly, so shutdown can never hang on this ordering.
            var transportStopTask = stopTask.ContinueWith(_ => WrappedTransport.Shutdown(),
                TaskContinuationOptions.ExecuteSynchronously).Unwrap();

            return Task.WhenAll(stopTask, transportStopTask)
                .ContinueWith(x => x.IsCompleted && !(x.IsFaulted || x.IsCanceled),
                    TaskContinuationOptions.ExecuteSynchronously);
        }
    }

    /// <summary>
    /// Base actor that queues transport adapter operations until its inbound listener is registered.
    /// </summary>
    public abstract class ActorTransportAdapterManager : UntypedActor
    {
        /// <summary>
        /// Lightweight Stash implementation
        /// </summary>
        protected Queue<object> DelayedEvents = new();

        /// <summary>
        /// Listener that receives inbound associations after setup completes.
        /// </summary>
        protected IAssociationEventListener AssociationListener;
        /// <summary>
        /// Local address bound by the underlying transport.
        /// </summary>
        protected Address LocalAddress;
        /// <summary>
        /// Counter used to generate unique child actor names.
        /// </summary>
        protected long UniqueId = 0L;

        /// <summary>
        /// Returns the next unique identifier for a child actor name.
        /// </summary>
        /// <returns>A monotonically increasing identifier.</returns>
        protected long NextId()
        {
            return Interlocked.Increment(ref UniqueId);
        }

        /// <summary>
        /// Registers the inbound listener, then delegates subsequent messages to <see cref="Ready"/>.
        /// </summary>
        /// <param name="message">Transport operation or other message received by the manager.</param>
        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ListenUnderlying listen:
                    LocalAddress = listen.ListenAddress;
                    var capturedSelf = Self;
                    listen.UpstreamListener.ContinueWith(
                        listenerRegistered => capturedSelf.Tell(new ListenerRegistered(listenerRegistered.Result)),
                        TaskContinuationOptions.ExecuteSynchronously);
                    break;
                
                case ListenerRegistered listener:
                    AssociationListener = listener.Listener;
                    foreach (var dEvent in DelayedEvents)
                    {
                        Self.Tell(dEvent, ActorRefs.NoSender);
                    }
                    DelayedEvents = new Queue<object>();
                    Context.Become(Ready);
                    break;
                
                default:
                    DelayedEvents.Enqueue(message);
                    break;
            }
        }

        /// <summary>
        /// Method to be implemented for child classes - processes messages once the transport is ready to send / receive
        /// </summary>
        /// <param name="message">Message to process after the wrapped transport is ready.</param>
        protected abstract void Ready(object message);
    }
}
