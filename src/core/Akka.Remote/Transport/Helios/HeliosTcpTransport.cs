//-----------------------------------------------------------------------
// <copyright file="HeliosTcpTransport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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

        /// <summary>
        /// Fires whenever a Helios <see cref="IConnection"/> gets closed.
        /// 
        /// Two possible causes for this event handler to fire:
        ///  * The other end of the connection has closed. We don't make any distinctions between graceful / unplanned shutdown.
        ///  * This end of the connection experienced an error.
        /// </summary>
        /// <param name="cause">An exception describing why the socket was closed.</param>
        /// <param name="closedChannel">The handle to the socket channel that closed.</param>
        protected override void OnDisconnect(HeliosConnectionException cause, IConnection closedChannel)
        {
            //if (cause != null)
            //    ChannelLocalActor.Notify(closedChannel, new UnderlyingTransportError(cause, "Underlying transport closed."));

            ChannelLocalActor.Notify(closedChannel, new Disassociated(DisassociateInfo.Unknown));
            ChannelLocalActor.Remove(closedChannel);
        }

        /// <summary>
        /// Fires whenever a Helios <see cref="IConnection"/> received data from the network.
        /// </summary>
        /// <param name="data">The message playload.</param>
        /// <param name="responseChannel">
        /// The channel responsible for sending the message.
        /// Can be used to send replies back.
        /// </param>
        protected override void OnMessage(NetworkData data, IConnection responseChannel)
        {
            if (data.Length > 0)
            {
                ChannelLocalActor.Notify(responseChannel, new InboundPayload(FromData(data)));
            }
        }

        /// <summary>
        /// Fires whenever a Helios <see cref="IConnection"/> experienced a non-fatal error.
        /// 
        /// <remarks>The connection should still be open even after this event fires.</remarks>
        /// </summary>
        /// <param name="ex">The execption that triggered this event.</param>
        /// <param name="erroredChannel">The handle to the Helios channel that errored.</param>
        protected override void OnException(Exception ex, IConnection erroredChannel)
        {
            // Want to notify only for encoding exceptions
            if(!(ex is HeliosConnectionException))
                ChannelLocalActor.Notify(erroredChannel, new UnderlyingTransportError(ex, "Non-fatal network error occurred inside underlying transport"));
        }

        public override void Dispose()
        {

            ChannelLocalActor.Remove(UnderlyingConnection);
            base.Dispose();
        }
    }

    /// <summary>
    /// TCP handlers for inbound connections
    /// </summary>
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

            }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.NotOnCanceled | TaskContinuationOptions.NotOnFaulted);
        }

        protected override void OnConnect(INode remoteAddress, IConnection responseChannel)
        {
            InitInbound(responseChannel, remoteAddress, NetworkData.Create(Node.Empty(), new byte[0], 0));
        }
    }

    /// <summary>
    /// TCP handlers for outbound connections
    /// </summary>
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
            if (!_channel.WasDisposed)
                _channel.Close();
        }
    }

    /// <summary>
    /// TCP implementation of a <see cref="HeliosTransport"/>.
    /// 
    /// <remarks>
    /// Due to the connection-oriented nature of TCP connections, this transport doesn't have to do any
    /// additional bookkeeping when transports are disposed or opened.
    /// </remarks>
    /// </summary>
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

            return ((TcpClientHandler)client).StatusFuture;
        }
    }
}

