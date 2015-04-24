using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Google.ProtocolBuffers;
using Helios.Exceptions;
using Helios.Net;
using Helios.Net.Bootstrap;
using Helios.Reactor;
using Helios.Reactor.Bootstrap;
using Helios.Topology;

namespace Akka.Remote.Transport.Helios
{
	class HeliosUdpTransport : HeliosTransport
	{
		public HeliosUdpTransport(ActorSystem system, Config config) : base(system, config)
		{
		}

		protected override IConnectionFactory ClientFactory
		{
			get
			{
				return new ClientBootstrap()
						.SetTransport(TransportType.Udp)
						.RemoteAddress(Node.Loopback())
						.Build();
			}
		}

		protected override IServerFactory ServerFactory
		{
			get
			{
				return new ServerBootstrap()
					.WorkerThreads(2)
					.SetTransport(TransportType.Udp)
					.Build();
			}
		}

		protected override IConnection NewServer(INode listenAddress)
		{
			return new UdpServerHandler(this, AssociationListenerPromise.Task, ServerFactory.NewReactor(listenAddress).ConnectionAdapter);
		}

		protected override IConnection NewClient(Address remoteAddress)
		{
			return new UdpClientHandler(this, remoteAddress, ClientFactory.NewConnection(AddressToNode(LocalAddress), AddressToNode(remoteAddress)));
		}

		protected override Task<AssociationHandle> AssociateInternal(Address remoteAddress)
		{
			var client = NewClient(remoteAddress);

			return ((UdpClientHandler)client).StatusFuture;
		}

		public override Task<Tuple<Address, TaskCompletionSource<IAssociationEventListener>>> Listen()
		{
			var listenAddress = NodeBuilder.BuildNode().Host(Settings.Hostname).WithPort(Settings.Port);
			var publicAddress = NodeBuilder.BuildNode().Host(Settings.PublicHostname).WithPort(Settings.Port);
			
			var server = NewServer(listenAddress);

			ConnectionGroup.TryAdd(server);
			ServerChannel = server;
			server.Open();
			publicAddress.Port = ServerChannel.Local.Port;

			var addr = NodeToAddress(publicAddress, SchemeIdentifier, System.Name, Settings.PublicHostname);
			if (addr == null) throw new HeliosNodeException("Unknown local address type {0}", server.Local);
			LocalAddress = addr;
			AssociationListenerPromise.Task.ContinueWith(result => ServerChannel.BeginReceive(),
				TaskContinuationOptions.AttachedToParent & TaskContinuationOptions.ExecuteSynchronously);

			return Task.Run(() => Tuple.Create(addr, AssociationListenerPromise));
		}
	}


	class UdpServerHandler : UdpHandlers
    {
        private Task<IAssociationEventListener> _associationListenerTask;

        public UdpServerHandler(HeliosTransport wrappedTransport, Task<IAssociationEventListener> associationListenerTask, IConnection underlyingConnection)
            : base(underlyingConnection)
        {
            WrappedTransport = wrappedTransport;
            _associationListenerTask = associationListenerTask;
        }

        protected void InitInbound(IConnection connection, INode remoteSocketAddress, NetworkData msg)
        {
            _associationListenerTask.ContinueWith(r =>
            {
                var listener = r.Result;
                var remoteAddress = HeliosTransport.NodeToAddress(remoteSocketAddress, WrappedTransport.SchemeIdentifier,
                    WrappedTransport.System.Name);

                if (remoteAddress == null) throw new HeliosNodeException("Unknown inbound remote address type {0}", remoteSocketAddress);
                AssociationHandle handle;
                Init(connection, remoteSocketAddress, remoteAddress, msg, out handle);
                listener.Notify(new InboundAssociation(handle));

            }, TaskContinuationOptions.AttachedToParent & TaskContinuationOptions.ExecuteSynchronously & TaskContinuationOptions.NotOnCanceled & TaskContinuationOptions.NotOnFaulted);
        }

        protected override void OnConnect(INode remoteAddress, IConnection responseChannel)
        {
            InitInbound(responseChannel, remoteAddress, NetworkData.Create(Node.Empty(), new byte[0], 0));
        }
    }

    class UdpClientHandler : UdpHandlers
    {
        protected readonly TaskCompletionSource<AssociationHandle> StatusPromise = new TaskCompletionSource<AssociationHandle>();

        public UdpClientHandler(HeliosTransport heliosWrappedTransport, Address remoteAddress, IConnection underlyingConnection)
            : base(underlyingConnection)
        {
            WrappedTransport = heliosWrappedTransport;
            RemoteAddress = remoteAddress;
        }

        public Task<AssociationHandle> StatusFuture { get { return StatusPromise.Task; } }

        protected Address RemoteAddress;

        protected void InitOutbound(IConnection channel, INode remoteSocketAddress, NetworkData msg)
        {
            AssociationHandle handle;
            Init(channel, remoteSocketAddress, RemoteAddress, msg, out handle);
            StatusPromise.SetResult(handle);
        }

        protected override void OnConnect(INode remoteAddress, IConnection responseChannel)
        {
            InitOutbound(responseChannel, remoteAddress, NetworkData.Create(Node.Empty(), new byte[0], 0));
        }

        protected override void OnDisconnect(HeliosConnectionException cause, IConnection closedChannel)
        {
            base.OnDisconnect(cause, closedChannel);
        }
    }

	abstract class UdpHandlers : CommonHandlers
    {
        protected UdpHandlers(IConnection underlyingConnection)
            : base(underlyingConnection)
        {
        }

        protected override void RegisterListener(IConnection channel, IHandleEventListener listener, NetworkData msg, INode remoteAddress)
        {
            ChannelLocalActor.Set(channel, listener);
            BindEvents(channel);
        }

        protected override AssociationHandle CreateHandle(IConnection channel, Address localAddress, Address remoteAddress)
        {
            return new UdpAssociationHandle(localAddress, remoteAddress, WrappedTransport, channel);
        }

        protected override void OnDisconnect(HeliosConnectionException cause, IConnection closedChannel)
        {
            if(cause != null)
                ChannelLocalActor.Notify(closedChannel, new UnderlyingTransportError(cause, "Underlying transport closed."));
            if (cause != null && cause.Type == ExceptionType.Closed)
                ChannelLocalActor.Notify(closedChannel, new Disassociated(DisassociateInfo.Shutdown));
            else
            {
                ChannelLocalActor.Notify(closedChannel, new Disassociated(DisassociateInfo.Unknown));
            }
                
            ChannelLocalActor.Remove(closedChannel);
        }

        protected override void OnMessage(NetworkData data, IConnection responseChannel)
        {
            if (data.Length > 0)
            {
                ChannelLocalActor.Notify(responseChannel, new InboundPayload(FromData(data)));
            }
        }

        protected override void OnException(Exception ex, IConnection erroredChannel)
        {
            ChannelLocalActor.Notify(erroredChannel, new UnderlyingTransportError(ex, "Non-fatal network error occurred inside underlying transport"));
        }

        public override void Dispose()
        {
           
            ChannelLocalActor.Remove(UnderlyingConnection);
            base.Dispose();
        }
    }

	class UdpAssociationHandle : AssociationHandle
    {
        private IConnection _channel;
        private HeliosTransport _transport;

        public UdpAssociationHandle(Address localAddress, Address remoteAddress, HeliosTransport transport, IConnection connection)
            : base(localAddress, remoteAddress)
        {
            _channel = connection;
            _transport = transport;
        }

        public override bool Write(ByteString payload)
        {
            if (_channel.IsOpen())
            {
                _channel.Send(HeliosHelpers.ToData(payload, RemoteAddress));
                return true;
            }
            return false;
        }

        public override void Disassociate()
        {
            if(!_channel.WasDisposed)
                _channel.Close();
        }
    }
}
