//-----------------------------------------------------------------------
// <copyright file="RemoteConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using Akka.Remote.TestKit.Proto;
using Akka.Remote.Transport.Helios;
using Helios.Buffers;
using Helios.Exceptions;
using Helios.Net;
using Helios.Net.Bootstrap;
using Helios.Reactor.Bootstrap;
using Helios.Serialization;
using Helios.Topology;

namespace Akka.Remote.TestKit
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal enum Role
    {
        Client,
        Server
    };

    /// <summary>
    /// Handler interface for receiving events from Helios
    /// </summary>
    public interface IHeliosConnectionHandler
    {
       void OnConnect(INode remoteAddress, IConnection responseChannel);

       void OnDisconnect(HeliosConnectionException cause, IConnection closedChannel);

       void OnMessage(object message, IConnection responseChannel);

       void OnException(Exception ex, IConnection erroredChannel);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class RemoteConnection : HeliosHelpers
    {
        private readonly MsgDecoder _msgDecoder;
        private readonly MsgEncoder _msgEncoder;
        private readonly ProtobufDecoder _protobufDecoder;
        private readonly IHeliosConnectionHandler _handler;

        public RemoteConnection(IConnection underlyingConnection, IHeliosConnectionHandler handler) : base(underlyingConnection)
        {
            _msgDecoder = new MsgDecoder();
            _msgEncoder = new MsgEncoder();
            _protobufDecoder = new ProtobufDecoder(TCP.Wrapper.DefaultInstance);
            _handler = handler;
        }

        protected override void OnConnect(INode remoteAddress, IConnection responseChannel)
        {
            //Got to pass OnMessage here or get null reference.
            responseChannel.BeginReceive(OnMessage);
            _handler.OnConnect(remoteAddress, responseChannel);
        }

        protected override void OnDisconnect(HeliosConnectionException cause, IConnection closedChannel)
        {
            _handler.OnDisconnect(cause, closedChannel);
        }

        protected override void OnMessage(NetworkData data, IConnection responseChannel)
        {
            var protoMessage = _protobufDecoder.Decode(data.Buffer);
            var testkitMesage = _msgDecoder.Decode(protoMessage);
            _handler.OnMessage(testkitMesage, responseChannel);
        }

        public void Write(object msg)
        {
            List<IByteBuf> protoMessages;
            _msgEncoder.Encode(this, msg, out protoMessages);
            foreach(var message in protoMessages)
                Send(NetworkData.Create(RemoteHost, message));
        }

        protected override void OnException(Exception ex, IConnection erroredChannel)
        {
            _handler.OnException(ex, erroredChannel);
        }

        #region Static methods

        public static RemoteConnection CreateConnection(Role role, INode socketAddress, int poolSize, IHeliosConnectionHandler upstreamHandler)
        {
            if (role == Role.Client)
            {
                var connection = new ClientBootstrap().SetTransport(TransportType.Tcp)
                    .SetOption("TcpNoDelay", true)
                    .SetEncoder(Encoders.DefaultEncoder) //LengthFieldPrepender
                    .SetDecoder(Encoders.DefaultDecoder) //LengthFieldFrameBasedDecoder
                    .WorkerThreads(poolSize).Build().NewConnection(socketAddress);
                var remoteConnection = new RemoteConnection(connection, upstreamHandler);
                remoteConnection.Open();
                return remoteConnection;
            }
            else //server
            {
                var connection = new ServerBootstrap().SetTransport(TransportType.Tcp)
                    .SetOption("TcpNoDelay", true)
                    .SetEncoder(Encoders.DefaultEncoder) //LengthFieldPrepender
                    .SetDecoder(Encoders.DefaultDecoder) //LengthFieldFrameBasedDecoder
                    .WorkerThreads(poolSize).Build().NewConnection(socketAddress);
                var remoteConnection = new RemoteConnection(connection, upstreamHandler);
                remoteConnection.Open();
                return remoteConnection;
            }
        }

        public static void Shutdown(IConnection connection)
        {
            //TODO: Correct?
            connection.Close();
        }

        #endregion
    }
}

