using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Remote.Transport
{
    public interface ITransportAdapterProvider
    {
        /// <summary>
        /// Create a transport adapter that wraps the underlying transport
        /// </summary>
        Transport Create(Transport wrappedTransport, ActorSystem system);
    }

    internal class TransportAdaptersExtension : ExtensionIdProvider<TransportAdapters>
    {
        public override TransportAdapters CreateExtension(ActorSystem system)
        {
            return new TransportAdapters(system);
        }

        #region Static methods

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
    internal class TransportAdapters : IExtension
    {
        public TransportAdapters(ActorSystem system)
        {
            System = system;
            Settings = ((RemoteActorRefProvider)system.Provider).RemoteSettings;
        }

        public ActorSystem System { get; private set; }

        protected RemoteSettings Settings;

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
                    // ReSharper disable once AssignNullToNotNullAttribute
                    var newAdapter = (ITransportAdapterProvider)Activator.CreateInstance(adapterTypeName);
                    _adaptersTable.Add(adapter.Key, newAdapter);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException(string.Format("Cannot initiate transport adapter {0}", adapter.Value), ex);
                }
            }

            return _adaptersTable;
        }

        public ITransportAdapterProvider GetAdapterProvider(string name)
        {
            if (_adaptersTable.ContainsKey(name))
            {
                return _adaptersTable[name];
            }

            throw new ArgumentException(string.Format("There is no registered transport adapter provider with name {0}", name));
        }
    }

    internal class SchemeAugmenter
    {
        public SchemeAugmenter(string addedSchemeIdentifier)
        {
            AddedSchemeIdentifier = addedSchemeIdentifier;
        }

        public readonly string AddedSchemeIdentifier;

        public string AugmentScheme(string originalScheme)
        {
            return string.Format("{0}.{1}", AddedSchemeIdentifier, originalScheme);
        }

        public Address AugmentScheme(Address address)
        {
            return address.Copy(protocol: AugmentScheme(address.Protocol));
        }

        public string RemoveScheme(string scheme)
        {
            if (scheme.StartsWith(string.Format("{0}.", AddedSchemeIdentifier)))
                return scheme.Remove(0, AddedSchemeIdentifier.Length + 1);
            return scheme;
        }

        public Address RemoveScheme(Address address)
        {
            return address.Copy(protocol: RemoveScheme(address.Protocol));
        }
    }

    /// <summary>
    /// An adapter that wraps a transport and provides interception capabilities
    /// </summary>
    internal abstract class AbstractTransportAdapter : Transport
    {
        protected AbstractTransportAdapter(Transport wrappedTransport)
        {
            WrappedTransport = wrappedTransport;
        }

        protected Transport WrappedTransport;

        protected abstract SchemeAugmenter SchemeAugmenter { get; }

        public override string SchemeIdentifier
        {
            get
            {
                return SchemeAugmenter.AugmentScheme(WrappedTransport.SchemeIdentifier);
            }
        }

        protected abstract Task<IAssociationEventListener> InterceptListen(Address listenAddress,
            Task<IAssociationEventListener> listenerTask);

        protected abstract void InterceptAssociate(Address remoteAddress,
            TaskCompletionSource<AssociationHandle> statusPromise);

        public override bool IsResponsibleFor(Address remote)
        {
            return WrappedTransport.IsResponsibleFor(remote);
        }

        public override Task<Tuple<Address, TaskCompletionSource<IAssociationEventListener>>> Listen()
        {
            var upstreamListenerPromise = new TaskCompletionSource<IAssociationEventListener>();
            return WrappedTransport.Listen().ContinueWith(async listenerTask =>
            {
                var listenAddress = listenerTask.Result.Item1;
                var listenerPromise = listenerTask.Result.Item2;
                listenerPromise.TrySetResult(await InterceptListen(listenAddress, upstreamListenerPromise.Task));
                return
                    new Tuple<Address, TaskCompletionSource<IAssociationEventListener>>(
                        SchemeAugmenter.AugmentScheme(listenAddress), upstreamListenerPromise);
            }, TaskContinuationOptions.ExecuteSynchronously & TaskContinuationOptions.AttachedToParent).Unwrap();
        }

        public override Task<AssociationHandle> Associate(Address remoteAddress)
        {
            var statusPromise = new TaskCompletionSource<AssociationHandle>();
            InterceptAssociate(SchemeAugmenter.RemoveScheme(remoteAddress), statusPromise);
            return statusPromise.Task;
        }

        public override Task<bool> Shutdown()
        {
            return WrappedTransport.Shutdown();
        }
    }

    internal abstract class AbstractTransportAdapterHandle : AssociationHandle
    {
        protected AbstractTransportAdapterHandle(Address originalLocalAddress, Address originalRemoteAddress, AssociationHandle wrappedHandle, string addedSchemeIdentifier) : base(originalLocalAddress, originalRemoteAddress)
        {
            WrappedHandle = wrappedHandle;
            OriginalRemoteAddress = originalRemoteAddress;
            OriginalLocalAddress = originalLocalAddress;
            SchemeAugmenter = new SchemeAugmenter(addedSchemeIdentifier);
            RemoteAddress = SchemeAugmenter.AugmentScheme(OriginalRemoteAddress);
            LocalAddress = SchemeAugmenter.AugmentScheme(OriginalLocalAddress);
        }

        public Address OriginalLocalAddress { get; private set; }

        public Address OriginalRemoteAddress { get; private set; }

        public AssociationHandle WrappedHandle { get; private set; }

        protected SchemeAugmenter SchemeAugmenter { get; private set; }

        protected bool Equals(AbstractTransportAdapterHandle other)
        {
            return Equals(OriginalLocalAddress, other.OriginalLocalAddress) && Equals(OriginalRemoteAddress, other.OriginalRemoteAddress) && Equals(WrappedHandle, other.WrappedHandle);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((AbstractTransportAdapterHandle) obj);
        }

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
    internal abstract class TransportOperation
    {
        public static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(5);
    }

    internal sealed class ListenerRegistered : TransportOperation
    {
        public ListenerRegistered(IAssociationEventListener listener)
        {
            Listener = listener;
        }

        public IAssociationEventListener Listener { get; private set; }
    }

    internal sealed class AssociateUnderlying : TransportOperation
    {
        public AssociateUnderlying(Address remoteAddress, TaskCompletionSource<AssociationHandle> statusPromise)
        {
            RemoteAddress = remoteAddress;
            StatusPromise = statusPromise;
        }

        public Address RemoteAddress { get; private set; }

        public TaskCompletionSource<AssociationHandle> StatusPromise { get; private set; }
    }

    internal sealed class ListenUnderlying : TransportOperation
    {
        public ListenUnderlying(Address listenAddress, Task<IAssociationEventListener> upstreamListener)
        {
            UpstreamListener = upstreamListener;
            ListenAddress = listenAddress;
        }

        public Address ListenAddress { get; private set; }

        public Task<IAssociationEventListener> UpstreamListener { get; private set; }
    }

    internal sealed class DisassociateUnderlying : TransportOperation
    {
        public DisassociateUnderlying(DisassociateInfo info = DisassociateInfo.Unknown)
        {
            Info = info;
        }

        public DisassociateInfo Info { get; private set; }
    }

    internal abstract class ActorTransportAdapter : AbstractTransportAdapter
    {
        protected ActorTransportAdapter(Transport wrappedTransport, ActorSystem system) : base(wrappedTransport)
        {
            System = system;
        }

        protected new ActorSystem System;

        protected abstract string ManagerName { get; }
        protected abstract Props ManagerProps { get; }

        protected volatile ActorRef manager;

        private Task<ActorRef> RegisterManager()
        {
            return System.ActorSelection("/system/transports").Ask<ActorRef>(new RegisterTransportActor(ManagerProps, ManagerName));
        }

        protected override Task<IAssociationEventListener> InterceptListen(Address listenAddress, Task<IAssociationEventListener> listenerTask)
        {
            return RegisterManager().ContinueWith(mgrTask =>
            {
                manager = mgrTask.Result;
                manager.Tell(new ListenUnderlying(listenAddress, listenerTask));
                return (IAssociationEventListener)new ActorAssociationEventListener(manager);
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        protected override void InterceptAssociate(Address remoteAddress, TaskCompletionSource<AssociationHandle> statusPromise)
        {
            manager.Tell(new AssociateUnderlying(remoteAddress, statusPromise));
        }

        public override Task<bool> Shutdown()
        {
            var stopTask = manager.GracefulStop(((RemoteActorRefProvider) System.Provider).RemoteSettings.FlushWait);
            var transportStopTask = WrappedTransport.Shutdown();
            return Task.WhenAll(stopTask, transportStopTask).ContinueWith(x => x.IsCompleted, TaskContinuationOptions.ExecuteSynchronously);
        }
    }

    internal abstract class ActorTransportAdapterManager : UntypedActor
    {
        /// <summary>
        /// Lightweight Stash implementation
        /// </summary>
       protected Queue<object> DelayedEvents = new Queue<object>();

        protected IAssociationEventListener associationListener;
        protected Address localAddress;
        protected long uniqueId = 0L;

        protected long nextId()
        {
            return Interlocked.Increment(ref uniqueId);
        }

        protected override void OnReceive(object message)
        {
            PatternMatch.Match(message)
                .With<ListenUnderlying>(listen =>
                {
                    localAddress = listen.ListenAddress;
                    var capturedSelf = Self;
                    listen.UpstreamListener.ContinueWith(
                        listenerRegistered => capturedSelf.Tell(new ListenerRegistered(listenerRegistered.Result)),
                        TaskContinuationOptions.AttachedToParent);
                })
                .With<ListenerRegistered>(listener =>
                {
                    associationListener = listener.Listener;
                    foreach (var dEvent in DelayedEvents)
                    {
                        Self.Tell(dEvent, ActorRef.NoSender);
                    }
                    DelayedEvents = new Queue<object>();
                    Context.Become(Ready);
                })
                .Default(m => DelayedEvents.Enqueue(m));
        }

        /// <summary>
        /// Method to be implemented for child classes - processes messages once the transport is ready to send / receive
        /// </summary>
        /// <param name="message"></param>
        protected abstract void Ready(object message);
    }
}
