//-----------------------------------------------------------------------
// <copyright file="UdpConnectedIntegrationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using Akka.Actor;
using Akka.IO;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.IO
{
    public class UdpConnectedIntegrationSpec : AkkaSpec
    {
        private readonly IPEndPoint[] _addresses;

        public UdpConnectedIntegrationSpec()
            : base(@"
                    akka.io.udp-connected.nr-of-selectors = 1
                    akka.io.udp-connected.direct-buffer-pool-limit = 100
                    akka.io.udp-connected.direct-buffer-size = 1024
                    akka.io.udp.nr-of-selectors = 1
                    akka.io.udp.direct-buffer-pool-limit = 100
                    akka.io.udp.direct-buffer-size = 1024
                    akka.loglevel = INFO
                    akka.actor.serialize-creators = on")
        {
            _addresses = TestUtils.TemporaryServerAddresses(3, udp: true).ToArray();
        }

        private IActorRef BindUdp(IPEndPoint address, IActorRef handler)
        {
            var commander = CreateTestProbe();
            commander.Send(Udp.Instance.Apply(Sys).Manager, new Udp.Bind(handler, address));
            commander.ExpectMsg<Udp.Bound>(x => x.LocalAddress.Equals(address)); 
            return commander.Sender;
        }

        private IActorRef ConnectUdp(IPEndPoint localAddress, IPEndPoint remoteAddress, IActorRef handler)
        {
            var commander = CreateTestProbe();
            commander.Send(UdpConnected.Instance.Apply(Sys).Manager, new UdpConnected.Connect(handler, remoteAddress, localAddress));
            commander.ExpectMsg<UdpConnected.Connected>();
            return commander.Sender;
        }

        [Fact]
        public void The_UDP_connection_oriented_implementation_must_be_able_to_send_and_receive_without_binding()
        {
            var serverAddress = _addresses[0];
            var server = BindUdp(serverAddress, TestActor);
            var data1 = ByteString.FromString("To infinity and beyond!");
            var data2 = ByteString.FromString("All your datagram belong to us");

            ConnectUdp(null, serverAddress, TestActor).Tell(UdpConnected.Send.Create(data1));

            var clientAddress = ExpectMsgPf(TimeSpan.FromSeconds(3), "", msg =>
            {
                var received = msg as Udp.Received;
                if (received != null)
                {
                    received.Data.ShouldBe(data1);
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
            var serverAddress = _addresses[0];
            var clientAddress = _addresses[1];
            var server = BindUdp(serverAddress, TestActor);
            var data1 = ByteString.FromString("To infinity and beyond!");
            var data2 = ByteString.FromString("All your datagram belong to us");
            ConnectUdp(clientAddress, serverAddress, TestActor).Tell(UdpConnected.Send.Create(data1));

            ExpectMsgPf(TimeSpan.FromSeconds(3), "", msg =>
            {
                var received = msg as Udp.Received;
                if (received != null)
                {
                    received.Data.ShouldBe(data1);
                    received.Sender.ShouldBe(clientAddress);
                    return received.Sender;
                }
                throw new Exception();
            });

            server.Tell(Udp.Send.Create(data2, clientAddress));

            ExpectMsg<UdpConnected.Received>(x => x.Data.ShouldBe(data2));
        }
    }
}
