//-----------------------------------------------------------------------
// <copyright file="RemoteConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Akka.Remote.TestKit.Proto;
using Akka.Remote.Transport.DotNetty;
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Codecs.Protobuf;
using DotNetty.Common.Internal.Logging;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Microsoft.Extensions.Logging;

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
    /// INTERNAL API
    /// </summary>
    internal class RemoteConnection
    {
        private static void ApplyChannelPipeline(IChannel channel, IChannelHandler handler)
        {
            var encoders = new IChannelHandler[]
                {new LengthFieldPrepender(ByteOrder.LittleEndian, 4, 0, false), new LengthFieldBasedFrameDecoder(ByteOrder.LittleEndian, 10000, 0, 4, 0, 4, true)};
            var protobuf = new IChannelHandler[] { new ProtobufEncoder(), new ProtobufDecoder(Proto.Msg.Wrapper.Parser) };
            var msg = new IChannelHandler[] { new MsgEncoder(), new MsgDecoder() };
            var pipeline = encoders.Concat(protobuf).Concat(msg);
            foreach (var h in pipeline)
                channel.Pipeline.AddLast(h);
            channel.Pipeline.AddLast("TestKitHandler", handler);
        }


        #region Static methods

        private static IEventLoopGroup _clientPool;
        private static IEventLoopGroup GetClientWorkerPool(int poolSize)
        {
            if (_clientPool == null)
            {
                _clientPool = new MultithreadEventLoopGroup(poolSize);
            }
            return _clientPool;
        }

        private static IEventLoopGroup _serverPool;
        private static IEventLoopGroup GetServerPool(int poolSize)
        {
            if (_serverPool == null)
            {
                _serverPool = new MultithreadEventLoopGroup(poolSize);
            }
            return _serverPool;
        }

        private static IEventLoopGroup _serverWorkerPool;

        private static IEventLoopGroup GetServerWorkerPool(int poolSize)
        {
            if (_serverWorkerPool == null)
            {
                _serverWorkerPool = new MultithreadEventLoopGroup(poolSize);
            }
            return _serverWorkerPool;
        }

        public static Task<IChannel> CreateConnection(Role role, IPEndPoint socketAddress, int poolSize, IChannelHandler upstreamHandler)
        {
            if (role == Role.Client)
            {
                var connection = new Bootstrap()
                    .ChannelFactory(() => new TcpSocketChannel(socketAddress.AddressFamily))
                    .Option(ChannelOption.TcpNodelay, true)
                    .Group(GetClientWorkerPool(poolSize))
                    .Handler(new ActionChannelInitializer<TcpSocketChannel>(channel =>
                    {
                        ApplyChannelPipeline(channel, upstreamHandler);
                    }));

                return connection.ConnectAsync(socketAddress);
            }
            else //server
            {
                var connection = new ServerBootstrap()
                    .Group(GetServerPool(poolSize), GetServerWorkerPool(poolSize))
                    .ChannelFactory(() => new TcpServerSocketChannel(socketAddress.AddressFamily))
                    .ChildOption(ChannelOption.TcpNodelay, true)
                    .ChildHandler(new ActionChannelInitializer<TcpSocketChannel>(channel =>
                    {
                        ApplyChannelPipeline(channel, upstreamHandler);
                    }));
                return connection.BindAsync(socketAddress);
            }
        }

        public static void Shutdown(IChannel connection)
        {
            var disconnectTimeout = TimeSpan.FromSeconds(2); //todo: make into setting loaded from HOCON
            if (!connection.CloseAsync().Wait(disconnectTimeout))
            {
                // LoggingFactory.GetLogger<RemoteConnection>().Warning("Failed to shutdown remote connection within {0}", disconnectTimeout);
            }

        }

        public static async Task ReleaseAll()
        {
            Task tc = _clientPool?.ShutdownGracefullyAsync() ?? TaskEx.Completed;
            Task ts = _serverPool?.ShutdownGracefullyAsync() ?? TaskEx.Completed;
            await Task.WhenAll(tc, ts).ConfigureAwait(false);
        }

        #endregion
    }
}
