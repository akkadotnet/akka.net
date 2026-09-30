//-----------------------------------------------------------------------
// <copyright file="UdpConnectedIntegrationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Tests.IO
{
    public class UdpConnectedIntegrationSpec : AkkaSpec
    {
        public UdpConnectedIntegrationSpec(ITestOutputHelper output)
            : base("""

                                       akka.actor.serialize-creators = on
                                       akka.actor.serialize-messages = on

                                       akka.io.udp-connected.nr-of-selectors = 1
                                       akka.io.udp.nr-of-selectors = 1
                                       akka.io.udp.trace-logging = true
                                       akka.loglevel = DEBUG
                   """, output)
        {
        }

        private async Task<(IActorRef, IPEndPoint)> BindUdpAsync(IActorRef handler)
        {
            var commander = CreateTestProbe();
            commander.Send(Udp.Instance.Apply(Sys).Manager, new Udp.Bind(handler, new IPEndPoint(IPAddress.Loopback, 0)));
            IPEndPoint localAddress = null;
            await commander.ExpectMsgAsync<Udp.Bound>(x => localAddress = (IPEndPoint)x.LocalAddress);
            return (commander.Sender, localAddress);
        }

        private async Task<(IActorRef, IPEndPoint)> ConnectUdpAsync(IPEndPoint localAddress, IPEndPoint remoteAddress, IActorRef handler)
        {
            var commander = CreateTestProbe();
            IPEndPoint realLocalAddress = null;
            commander.Send(
                UdpConnected.Instance.Apply(Sys).Manager,
                new UdpConnected.Connect(handler, remoteAddress, localAddress, [
                    new TestSocketOption(socket => realLocalAddress = (IPEndPoint)socket.LocalEndPoint)
                ]));
            await commander.ExpectMsgAsync<UdpConnected.Connected>();
            return (commander.Sender, realLocalAddress);
        }

        private async Task<(IActorRef, IPEndPoint)> ConnectUdpAsync(IPEndPoint remoteAddress, IActorRef handler)
        {
            var commander = CreateTestProbe();
            IPEndPoint clientEndpoint = null;
            commander.Send(
                UdpConnected.Instance.Apply(Sys).Manager,
                new UdpConnected.Connect(handler, remoteAddress, options:
                [
                    new TestSocketOption(socket =>
                        clientEndpoint = (IPEndPoint)socket.LocalEndPoint)
                ]));
            await commander.ExpectMsgAsync<UdpConnected.Connected>();
            return (commander.Sender, clientEndpoint);
        }

        [Fact]
        public async Task The_UDP_connection_oriented_implementation_must_be_able_to_send_and_receive_without_binding()
        {
            var (server, serverLocalEndpoint) = await BindUdpAsync(TestActor);
            var data1 = Encoding.ASCII.GetBytes("To infinity and beyond!").AsMemory();
            var data2 = Encoding.ASCII.GetBytes("All your datagram belong to us").AsMemory();

            var (client, clientLocalEndpoint) =await ConnectUdpAsync(null, serverLocalEndpoint, TestActor);
            client.Tell(UdpConnected.Send.Create(data1));

            var clientAddress = await ExpectMsgOfAsync(TimeSpan.FromSeconds(3), "", msg =>
            {
                if (msg is not Udp.Received received) throw new Exception();
                received.Data.Span.SequenceEqual(data1.Span).Should().BeTrue();
                received.Sender.ShouldBe(clientLocalEndpoint);
                return received.Sender;
            });

            server.Tell(Udp.Send.Create(data2, clientAddress));

            await ExpectMsgAsync<UdpConnected.Received>(x => x.Data.Span.SequenceEqual(data2.Span).Should().BeTrue());
        }

        [Fact]
        public async Task The_UDP_connection_oriented_implementation_must_be_able_to_send_and_receive_with_binding()
        {
            var serverProbe = CreateTestProbe();
            var (server, serverLocalEndpoint) = await BindUdpAsync(serverProbe);
            var data1 = Encoding.ASCII.GetBytes("To infinity and beyond!").AsMemory();
            var data2 = Encoding.ASCII.GetBytes("All your datagram belong to us").AsMemory();
            var clientProbe = CreateTestProbe();
            var (client, clientLocalEndpoint) = await ConnectUdpAsync(serverLocalEndpoint, clientProbe);
            client.Tell(UdpConnected.Send.Create(data1));

            await ExpectMsgOfAsync(TimeSpan.FromSeconds(3), "", serverProbe, msg =>
            {
                if (msg is not Udp.Received received) throw new Exception();
                received.Data.Span.SequenceEqual(data1.Span).Should().BeTrue();
                return received.Sender;
            });

            server.Tell(Udp.Send.Create(data2, clientLocalEndpoint));

            await clientProbe.ExpectMsgAsync<UdpConnected.Received>(x => x.Data.Span.SequenceEqual(data2.Span).Should().BeTrue());
        }

        [Fact]
        public async Task The_UDP_connection_oriented_implementation_must_to_send_batch_writes_and_reads()
        {
            var serverProbe = CreateTestProbe();
            var (server, serverEndPoint) = await BindUdpAsync(serverProbe);
            var clientProbe = CreateTestProbe();
            var (client, clientEndPoint) = await ConnectUdpAsync(serverEndPoint, clientProbe);

            var data = Encoding.ASCII.GetBytes("Fly little packet!").AsMemory();

            // queue 3 writes
            client.Tell(UdpConnected.Send.Create(data));
            client.Tell(UdpConnected.Send.Create(data));
            client.Tell(UdpConnected.Send.Create(data));

            var raw = await serverProbe.ReceiveNAsync(3, default).ToListAsync();
            var serverMsgs = raw.Cast<Udp.Received>();
            serverMsgs.Sum(x => x.Data.Length).Should().Be(data.Length * 3);
            await serverProbe.ExpectNoMsgAsync(100.Milliseconds());

            // repeat in the other direction
            server.Tell(Udp.Send.Create(data, clientEndPoint));
            server.Tell(Udp.Send.Create(data, clientEndPoint));
            server.Tell(Udp.Send.Create(data, clientEndPoint));

            raw = await clientProbe.ReceiveNAsync(3, default).ToListAsync();
            var clientMsgs = raw.Cast<UdpConnected.Received>();
            clientMsgs.Sum(x => x.Data.Length).Should().Be(data.Length * 3);
        }

    }
}
