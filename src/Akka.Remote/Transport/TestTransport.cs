using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Google.ProtocolBuffers;

namespace Akka.Remote.Transport
{
    /// <summary>
    /// Transport implementation used for testing.
    /// 
    /// The TestTransport is basically shared memory between actor systems. It can be programmed to emulate
    /// different failure modes of a <see cref="Transport"/> implementation. TestTransport keeps a log of the activities
    /// it was requested to do. This class is not optimized for performance and MUST not be used in production systems.
    /// </summary>
    public class TestTransport : Transport
    {
        public readonly Address LocalAddress;

        public TestTransport(ActorSystem system, Config config, Address localAddress) : base(system, config)
        {
            LocalAddress = localAddress;
        }

        public override Address Listen()
        {
            throw new System.NotImplementedException();
        }

        public override bool IsResponsibleFor(Address remote)
        {
            return true;
        }

        public override Task<AssociationHandle> Associate(Address remoteAddress)
        {
            throw new NotImplementedException();
        }

        public bool Write(TestAssociationHandle handle, ByteString payload)
        {
            throw new System.NotImplementedException();
        }

        public void Disassociate(TestAssociationHandle handle) { }
    }

    /// <summary>
    /// Base trait for remote activities that are logged by <see cref="TestTransport"/>
    /// </summary>
    public abstract class Activity { }

    public sealed class ListenAttempt : Activity
    {
        public ListenAttempt(Address boundAddress)
        {
            BoundAddress = boundAddress;
        }

        public Address BoundAddress { get; private set; }
    }

    public sealed class AssociateAttempt : Activity
    {
        public AssociateAttempt(Address localAddress, Address remoteAddress)
        {
            RemoteAddress = remoteAddress;
            LocalAddress = localAddress;
        }

        public Address LocalAddress { get; private set; }

        public Address RemoteAddress { get; private set; }
    }

    public sealed class ShutdownAttempt : Activity
    {
        public ShutdownAttempt(Address boundAddress)
        {
            BoundAddress = boundAddress;
        }

        public Address BoundAddress { get; private set; }
    }

    public sealed class WriteAttempt : Activity
    {
        public WriteAttempt(Address sender, Address recipient, ByteString payload)
        {
            Payload = payload;
            Recipient = recipient;
            Sender = sender;
        }

        public Address Sender { get; private set; }

        public Address Recipient { get; private set; }

        public ByteString Payload { get; private set; }
    }

    public sealed class DisassociateAttempt : Activity
    {
        public DisassociateAttempt(Address requestor, Address remote)
        {
            Remote = remote;
            Requestor = requestor;
        }

        public Address Requestor { get; private set; }

        public Address Remote { get; private set; }
    }

    /// <summary>
    /// Shared state among <see cref="TestTransport"/> instances. Coordinates the transports and the means of
    /// communication between them.
    /// </summary>
    public class AssociationRegistry
    {
        private readonly ConcurrentStack<Activity> _activityLog = new ConcurrentStack<Activity>();
        private readonly ConcurrentDictionary<Address, Tuple<TestTransport, Task<IAssociationEventListener>>> _transportTable = new ConcurrentDictionary<Address, Tuple<TestTransport, Task<IAssociationEventListener>>>();
        private readonly ConcurrentDictionary<Tuple<Address, Address>, Tuple<IHandleEventListener, IHandleEventListener>> _listenersTable = new ConcurrentDictionary<Tuple<Address, Address>, Tuple<IHandleEventListener, IHandleEventListener>>();

        private static ConcurrentDictionary<string, AssociationRegistry> registries = new ConcurrentDictionary<string, AssociationRegistry>();

        public static AssociationRegistry Get(string key)
        {
            return registries.GetOrAdd(key, new AssociationRegistry());
        }

        public static void Clear()
        {
            registries.Clear();
        }

        /// <summary>
        /// Private constructor - used to enforce the Singleton (one <see cref="AssociationRegistry"/> per <see cref="ActorSystem"/>) nature of the class
        /// </summary>
        private AssociationRegistry()
        {
            
        }

        /// <summary>
        /// Returns the remote endpoint for a pair of endpoints relative to the owner of the supplied <see cref="TestAssociationHandle"/>.
        /// </summary>
        /// <param name="handle">The reference handle to determine the remote endpoint relative to</param>
        /// <param name="listenerPair">pair of listeners in initiator, receiver order</param>
        /// <returns></returns>
        public IHandleEventListener RemoteListenerRelativeTo(TestAssociationHandle handle,
            Tuple<IHandleEventListener, IHandleEventListener> listenerPair)
        {
            if (handle.Inbound)
                return listenerPair.Item1; //initiator
            return listenerPair.Item2; //receiver
        }

        /// <summary>
        /// Logs a transport activity
        /// </summary>
        /// <param name="activity">The activity to be logged</param>
        public void LogActivity(Activity activity)
        {
            _activityLog.Push(activity);
        }

        /// <summary>
        /// Gets a snapshot of the current transport activity log
        /// </summary>
        /// <returns>A IList of activities ordered left-to-right in chronological order (element[0] is the oldest)</returns>
        public IList<Activity> LogSnapshot()
        {
            return _activityLog.Reverse().ToList();
        }

        /// <summary>
        /// Clears the current contents of the log
        /// </summary>
        public void ClearLog()
        {
            _activityLog.Clear();
        }

        /// <summary>
        /// Records a mapping between an address and the corresponding (transport, associationEventListener) pair.
        /// </summary>
        /// <param name="transport">The transport that is to be registered. The address of this transport will be used as a key.</param>
        /// <param name="associationEventListenerTask">The Task that will be completed with the listener that will handle the events for the given transport.</param>
        public void RegisterTransport(TestTransport transport, Task<IAssociationEventListener> associationEventListenerTask)
        {
            _transportTable.TryAdd(transport.LocalAddress, new Tuple<TestTransport, Task<IAssociationEventListener>>(transport, associationEventListenerTask));
        }

        /// <summary>
        /// Indicates if all given transports were successfully registered. No assications can be established between
        /// transports that are not yet registered.
        /// </summary>
        /// <param name="addresses">The listen addresses of transports that participate in the test case.</param>
        /// <returns>True if all transports are successfully registered.</returns>
        public bool TransportsReady(IList<Address> addresses)
        {
            return addresses.All(x => _transportTable.ContainsKey(x));
        }

        /// <summary>
        /// Registers two event listeners corresponding to the two endpoints of an association.
        /// </summary>
        /// <param name="key">Ordered pair of addresses representing an association. First element must be the address of the initiator.</param>
        /// <param name="listeners">A pair of listeners that will be responsible for handling the events of the two endpoints
        /// of the association. Elements in the Tuple must be in the same order as the addresses in <see cref="key"/>.</param>
        public void RegisterListenerPair(Tuple<Address, Address> key,
            Tuple<IHandleEventListener, IHandleEventListener> listeners)
        {
            _listenersTable.TryAdd(key, listeners);
        }

        /// <summary>
        /// Removes an association.
        /// </summary>
        /// <param name="key">Ordered pair of addresses representing an association. First element must be the address of the initiator.</param>
        /// <returns>The original entries, or null if the key wasn't found in the table.</returns>
        public Tuple<IHandleEventListener, IHandleEventListener> DeregisterAssociation(Tuple<Address, Address> key)
        {
            Tuple<IHandleEventListener, IHandleEventListener> listeners;
            _listenersTable.TryRemove(key, out listeners);
            return listeners;
        }

        /// <summary>
        /// Tests if an association was registered.
        /// </summary>
        /// <param name="initiatorAddress">The initiator of the association.</param>
        /// <param name="remoteAddress">The other address of the association.</param>
        /// <returns>True if there is an association for the given address.</returns>
        public bool ExistsAssociation(Address initiatorAddress, Address remoteAddress)
        {
            return _listenersTable.ContainsKey(new Tuple<Address, Address>(initiatorAddress, remoteAddress));
        }

        /// <summary>
        /// Returns the event handler corresponding to the remote endpoint of the givne local handle. In other words
        /// it returns the listener that will receive <see cref="InboundPayload"/> events when <seealso cref="AssociationHandle.Write"/> is called.
        /// </summary>
        /// <param name="localHandle">The handle</param>
        /// <returns>The option that contains the listener if it exists.</returns>
        public IHandleEventListener GetRemoteReadHandlerFor(TestAssociationHandle localHandle)
        {
            Tuple<IHandleEventListener,IHandleEventListener> listeners;
            if (_listenersTable.TryGetValue(localHandle.Key, out listeners))
            {
                return RemoteListenerRelativeTo(localHandle, listeners);
            }

            return null;
        }

        /// <summary>
        /// Returns the transport bound to the given address.
        /// </summary>
        /// <param name="address">The address bound to the transport.</param>
        /// <returns>The transport, if it exists.</returns>
        public Tuple<TestTransport, Task<IAssociationEventListener>> TransportFor(Address address)
        {
            Tuple<TestTransport, Task<IAssociationEventListener>> transport;
            _transportTable.TryGetValue(address, out transport);
            return transport;
        }

        /// <summary>
        /// Clears the state of the entire registry.
        /// 
        /// <remarks>
        /// This method is not atomic and does not use a critical section when clearing transports, listeners, and logs.
        /// </remarks>
        /// </summary>
        public void Reset()
        {
            ClearLog();
            _transportTable.Clear();
            _listenersTable.Clear();
        }
    }

    public sealed class TestAssociationHandle : AssociationHandle
    {
        public TestAssociationHandle(Address localAddress, Address remoteAddress, bool inbound, TestTransport transport) : base(localAddress, remoteAddress)
        {
            Inbound = inbound;
            _transport = transport;
        }

        public readonly bool Inbound;

        internal volatile bool Writeable = true;

        private readonly TestTransport _transport;

        /// <summary>
        /// Key used in <see cref="AssociationRegistry"/> to identify associations. Contains an ordered Tuple of addresses,
        /// where the first address is always the initiator of the association.
        /// </summary>
        public Tuple<Address, Address> Key
        {
            get
            {
                return !Inbound ? new Tuple<Address, Address>(LocalAddress, RemoteAddress) : new Tuple<Address, Address>(RemoteAddress, LocalAddress);
            }
        }

        public override bool Write(ByteString payload)
        {
            return Writeable && _transport.Write(this, payload);
        }

        public override void Disassociate()
        {
            _transport.Disassociate(this);
        }
    }
}
