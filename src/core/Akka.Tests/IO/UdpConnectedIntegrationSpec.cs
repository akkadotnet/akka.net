//-----------------------------------------------------------------------
// <copyright file="UdpConnectedIntegrationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using Akka.Actor;
using Akka.IO;
using Akka.IO.Buffers;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.IO
{
    public class UdpConnectedIntegrationSpec : AkkaSpec
    {
        public UdpConnectedIntegrationSpec(ITestOutputHelper output)
            : base(@"
                    akka.actor.serialize-creators = on
                    akka.actor.serialize-messages = on

                    akka.io.udp-connected.buffer-pool = ""akka.io.udp-connected.direct-buffer-pool""
                    akka.io.udp-connected.nr-of-selectors = 1
                    # This comes out to be about 1.6 Mib maximum total buffer size
                    akka.io.udp-connected.direct-buffer-pool.buffer-size = 512
                    akka.io.udp-connected.direct-buffer-pool.buffers-per-segment = 32
                    akka.io.udp-connected.direct-buffer-pool.buffer-pool-limit = 100

                    akka.io.udp.buffer-pool = ""akka.io.udp.direct-buffer-pool""
                    akka.io.udp.nr-of-selectors = 1
                    # This comes out to be about 1.6 Mib maximum total buffer size
                    akka.io.udp.direct-buffer-pool.buffer-size = 512
                    akka.io.udp.direct-buffer-pool.buffers-per-segment = 32
                    akka.io.udp.direct-buffer-pool.buffer-pool-limit = 100
                    akka.io.udp.trace-logging = true
                    akka.loglevel = DEBUG", output)
        {
        }

        private (IActorRef, IPEndPoint) BindUdp(IActorRef handler)
        {
            var commander = CreateTestProbe();
            commander.Send(Udp.Instance.Apply(Sys).Manager, new Udp.Bind(handler, new IPEndPoint(IPAddress.Loopback, 0)));
            IPEndPoint localAddress = null; 
            commander.ExpectMsg<Udp.Bound>(x => localAddress = (IPEndPoint)x.LocalAddress); 
            return (commander.Sender, localAddress);
        }

        private (IActorRef, IPEndPoint) ConnectUdp(IPEndPoint localAddress, IPEndPoint remoteAddress, IActorRef handler)
        {
            var commander = CreateTestProbe();
            IPEndPoint realLocalAddress = null; 
            commander.Send(
                UdpConnected.Instance.Apply(Sys).Manager, 
                new UdpConnected.Connect(handler, remoteAddress, localAddress, new []
                {
                    new TestSocketOption(socket => realLocalAddress = (IPEndPoint)socket.LocalEndPoint)
                }));
            commander.ExpectMsg<UdpConnected.Connected>();
            return (commander.Sender, realLocalAddress);
        }

        private (IActorRef, IPEndPoint) ConnectUdp(IPEndPoint remoteAddress, IActorRef handler)
        {
            var commander = CreateTestProbe();
            IPEndPoint clientEndpoint = null; 
            commander.Send(
                UdpConnected.Instance.Apply(Sys).Manager, 
                new UdpConnected.Connect(handler, remoteAddress, options:new []
                {
                    new TestSocketOption(socket => 
                        clientEndpoint = (IPEndPoint)socket.LocalEndPoint)
                }));
            commander.ExpectMsg<UdpConnected.Connected>();
            return (commander.Sender, clientEndpoint);
        }

        [Fact]
        public void The_UDP_connection_oriented_implementation_must_be_able_to_send_and_receive_without_binding()
        {
            var (server, serverLocalEndpoint) = BindUdp(TestActor);
            var data1 = ByteString.FromString("To infinity and beyond!");
            var data2 = ByteString.FromString("All your datagram belong to us");

            var (client, clientLocalEndpoint) = ConnectUdp(null, serverLocalEndpoint, TestActor);
            client.Tell(UdpConnected.Send.Create(data1));

            var clientAddress = ExpectMsgPf(TimeSpan.FromSeconds(3), "", msg =>
            {
                if (msg is Udp.Received received)
                {
                    received.Data.ShouldBe(data1);
                    received.Sender.ShouldBe(clientLocalEndpoint);
                    return received.Sender;
                }
                throw new Exception();
            });

            server.Tell(Udp.Send.Create(data2, clientAddress));

            ExpectMsg<UdpConnected.Received>(x => x.Data.ShouldBe(data2));
        }

        [Fact]
        public void The_UDP_connection_oriented_implementation_must_be_able_to_send_and_receive_with_binding()
        {
            var serverProbe = CreateTestProbe();
            var (server, serverLocalEndpoint) = BindUdp(serverProbe);
            var data1 = ByteString.FromString("To infinity") + ByteString.FromString(" and beyond!");
            var data2 = ByteString.FromString("All your datagram belong to us");
            var clientProbe = CreateTestProbe();
            var (client, clientLocalEndpoint) = ConnectUdp(serverLocalEndpoint, clientProbe);
            client.Tell(UdpConnected.Send.Create(data1));

            ExpectMsgPf(TimeSpan.FromSeconds(3), "", serverProbe, msg =>
            {
                if (msg is Udp.Received received)
                {
                    received.Data.ShouldBe(data1);
                    return received.Sender;
                }
                throw new Exception();
            });

            server.Tell(Udp.Send.Create(data2, clientLocalEndpoint));

            clientProbe.ExpectMsg<UdpConnected.Received>(x => x.Data.ShouldBe(data2));
        }

        [Fact]
        public void The_UDP_connection_oriented_implementation_must_to_send_batch_writes_and_reads()
        {
            var serverProbe = CreateTestProbe();
            var (server, serverEndPoint) = BindUdp(serverProbe);
            var clientProbe = CreateTestProbe();
            var (client, clientEndPoint) = ConnectUdp(serverEndPoint, clientProbe);
            
            var data = ByteString.FromString("Fly little packet!");

            // queue 3 writes
            client.Tell(UdpConnected.Send.Create(data));
            client.Tell(UdpConnected.Send.Create(data));
            client.Tell(UdpConnected.Send.Create(data));

            var raw = serverProbe.ReceiveN(3);
            var serverMsgs = raw.Cast<Udp.Received>();
            serverMsgs.Sum(x => x.Data.Count).Should().Be(data.Count * 3);
            serverProbe.ExpectNoMsg(100.Milliseconds());

            // repeat in the other direction
            server.Tell(Udp.Send.Create(data, clientEndPoint));
            server.Tell(Udp.Send.Create(data, clientEndPoint));
            server.Tell(Udp.Send.Create(data, clientEndPoint));

            raw = clientProbe.ReceiveN(3);
            var clientMsgs = raw.Cast<UdpConnected.Received>();
            clientMsgs.Sum(x => x.Data.Count).Should().Be(data.Count * 3);
        }
        
        [Fact]
        public void The_UDP_connection_oriented_implementation_must_not_leak_memory()
        {
            const int batchCount = 2000;
            const int batchSize = 100;
            
            var udpConnection = UdpConnected.Instance.Apply(Sys);
            var poolInfo = udpConnection.SocketEventArgsPool.BufferPoolInfo;
            poolInfo.Type.Should().Be(typeof(DirectBufferPool));
            poolInfo.Free.Should().Be(poolInfo.TotalSize);
            poolInfo.Used.Should().Be(0);
            
            var serverProbe = CreateTestProbe();
            var (server, serverEndPoint) = BindUdp(serverProbe);

            var clientProbe = CreateTestProbe();
            var (client, clientEndPoint) = ConnectUdp(serverEndPoint, clientProbe);
            
            var data = ByteString.FromString("Fly little packet!");

            // send a lot of packets through, the byte buffer pool should not leak anything
            for (var n = 0; n < batchCount; ++n)
            {
                for (var j = 0; j < batchSize; ++j)
                    client.Tell(UdpConnected.Send.Create(data));

                var msgs = serverProbe.ReceiveN(batchSize, TimeSpan.FromSeconds(10));
                var cast = msgs.Cast<Udp.Received>();
                cast.Sum(m => m.Data.Count).Should().Be(data.Count * batchSize);
            }

            // stop all connections so all receives are stopped and all pending SocketAsyncEventArgs are collected
            server.Tell(Udp.Unbind.Instance, serverProbe);
            serverProbe.ExpectMsg<Udp.Unbound>();
            client.Tell(UdpConnected.Disconnect.Instance, clientProbe);
            clientProbe.ExpectMsg<UdpConnected.Disconnected>();
            
            // wait for all SocketAsyncEventArgs to be released
            Thread.Sleep(1000);
            
            poolInfo = udpConnection.SocketEventArgsPool.BufferPoolInfo;
            poolInfo.Type.Should().Be(typeof(DirectBufferPool));
            poolInfo.Free.Should().Be(poolInfo.TotalSize);
            poolInfo.Used.Should().Be(0);
        }
    }
}
