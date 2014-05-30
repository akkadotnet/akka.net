using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Google.ProtocolBuffers;
using Helios.Exceptions;
using Helios.Net;
using Helios.Topology;

namespace Akka.Remote.Transport.Helios
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    static class ChannelLocalActor
    {
        private static ConcurrentDictionary<IConnection, IHandleEventListener> _channelActors = new ConcurrentDictionary<IConnection, IHandleEventListener>();

        public static void Set(IConnection channel, IHandleEventListener listener = null)
        {
            _channelActors.AddOrUpdate(channel, listener, (connection, eventListener) => listener);
        }

        public static void Remove(IConnection channel)
        {
            IHandleEventListener listener;
            _channelActors.TryRemove(channel, out listener);
        }

        public static void Notify(IConnection channel, IHandleEvent msg)
        {
            IHandleEventListener listener;

            if (_channelActors.TryGetValue(channel, out listener))
            {
                listener.Notify(msg);
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    abstract class TcpHandlers : CommonHandlers
    {
        protected TcpHandlers(IConnection underlyingConnection)
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
            return new TcpAssociationHandle(localAddress, remoteAddress, WrappedTransport, channel);
        }

        protected override void OnDisconnect(HeliosConnectionException cause, IConnection closedChannel)
        {
            if(cause != null && cause.Type == ExceptionType.Closed)
                ChannelLocalActor.Notify(closedChannel, new Disassociated(DisassociateInfo.Shutdown));
            else
                ChannelLocalActor.Notify(closedChannel, new Disassociated(DisassociateInfo.Unknown));
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
            ChannelLocalActor.Notify(erroredChannel, new Disassociated(DisassociateInfo.Unknown));
            erroredChannel.Close();
        }

        public override void Dispose()
        {
           
            ChannelLocalActor.Remove(UnderlyingConnection);
            base.Dispose();
        }
    }

    class TcpServerHandler : TcpHandlers
    {
        private Task<IAssociationEventListener> _associationListenerTask;

        public TcpServerHandler(HeliosTransport wrappedTransport, Task<IAssociationEventListener> associationListenerTask, IConnection underlyingConnection)
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

    class TcpClientHandler : TcpHandlers
    {
        protected readonly TaskCompletionSource<AssociationHandle> StatusPromise = new TaskCompletionSource<AssociationHandle>();

        public TcpClientHandler(HeliosTransport heliosWrappedTransport, Address remoteAddress, IConnection underlyingConnection)
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

    /// <summary>
    /// INTERNAL API
    /// </summary>
    class TcpAssociationHandle : AssociationHandle
    {
        private IConnection _channel;
        private HeliosTransport _transport;

        public TcpAssociationHandle(Address localAddress, Address remoteAddress, HeliosTransport transport, IConnection connection)
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

    class HeliosTcpTransport : HeliosTransport
    {
        public HeliosTcpTransport(ActorSystem system, Config config)
            : base(system, config)
        {
        }

        protected override Task<AssociationHandle> AssociateInternal(Address remoteAddress)
        {
            var client = NewClient(remoteAddress);

            var socketAddress = client.RemoteHost;
            client.Open();

            return client.AsInstanceOf<TcpClientHandler>().StatusFuture;
        }
    }
}