using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        protected readonly Address LocalAddress;

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
        private List<Activity>  _activityLog = new List<Activity>();
        private ConcurrentDictionary<Address, Tuple<TestTransport, Task<IAssociationEventListener>>> _transportTable = new ConcurrentDictionary<Address, Tuple<TestTransport, Task<IAssociationEventListener>>>();
        private ConcurrentDictionary<Tuple<Address, Address>, Tuple<IHandleEventListener, IHandleEventListener>> _listenersTable = new ConcurrentDictionary<Tuple<Address, Address>, Tuple<IHandleEventListener, IHandleEventListener>>();


    }

    public sealed class TestAssociationHandle : AssociationHandle
    {
        public TestAssociationHandle(Address localAddress, Address remoteAddress, bool inbound, TestTransport transport) : base(localAddress, remoteAddress)
        {
            _inbound = inbound;
            _transport = transport;
        }

        readonly bool _inbound;

        internal volatile bool Writeable = true;

        private readonly TestTransport _transport;

        public Tuple<Address, Address> Key
        {
            get
            {
                return !_inbound ? new Tuple<Address, Address>(LocalAddress, RemoteAddress) : new Tuple<Address, Address>(RemoteAddress, LocalAddress);
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
