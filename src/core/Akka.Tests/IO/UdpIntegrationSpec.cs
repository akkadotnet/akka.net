//-----------------------------------------------------------------------
// <copyright file="UdpIntegrationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Akka.Actor;
using Akka.IO;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Tests.IO
{
    public class UdpIntegrationSpec : AkkaSpec
    {
        private readonly IPEndPoint[] _addresses;

        public UdpIntegrationSpec()
            : base(@"
                    akka.io.udp.max-channels = unlimited
                    akka.io.udp.nr-of-selectors = 1
                    akka.io.udp.direct-buffer-pool-limit = 100
                    akka.io.udp.direct-buffer-size = 1024
                    akka.loglevel = INFO
                    akka.actor.serialize-creators = on")
        {
            _addresses = TestUtils.TemporaryServerAddresses(6, udp: true).ToArray();
        }

        private IActorRef BindUdp(IPEndPoint address, IActorRef handler)
        {
            var commander = CreateTestProbe();
            commander.Send(Udp.Instance.Apply(Sys).Manager, new Udp.Bind(handler, address));
            commander.ExpectMsg<Udp.Bound>(x => x.LocalAddress.Equals(address));
            return commander.Sender;
        }

        private IActorRef SimpleSender()
        {
            var commander = CreateTestProbe();
            commander.Send(Udp.Instance.Apply(Sys).Manager, Udp.SimpleSender.Instance);
            commander.ExpectMsg<Udp.SimpleSenderReady>(TimeSpan.FromSeconds(10));
            return commander.Sender;
        }

        [Fact]
        public void The_UDP_Fire_and_Forget_implementation_must_be_able_to_send_without_binding()
        {
            var serverAddress = _addresses[0];
            var server = BindUdp(serverAddress, TestActor);
            var data = ByteString.FromString("To infinity and beyond!");
            SimpleSender().Tell(Udp.Send.Create(data, serverAddress));

            ExpectMsg<Udp.Received>(x => x.Data.ShouldBe(data));
        }

        [Fact]
        public void The_UDP_Fire_and_Forget_implementation_must_be_able_to_send_several_packet_back_and_forth_with_binding()
        {
            var serverAddress = _addresses[0];
            var clientAddress = _addresses[1];
            var server = BindUdp(serverAddress, TestActor);
            var client = BindUdp(clientAddress, TestActor);
            var data = ByteString.FromString("Fly little packet!");

            Action checkSendingToClient = () =>
            {
                server.Tell(Udp.Send.Create(data, clientAddress));
                ExpectMsg<Udp.Received>(x =>
                {
                    x.Data.ShouldBe(data);
                    x.Sender.ShouldBe(serverAddress);
                });
            };
            Action checkSendingToServer = () =>
            {
                client.Tell(Udp.Send.Create(data, serverAddress));
                ExpectMsg<Udp.Received>(x =>
                {
                    x.Data.ShouldBe(data);
                    x.Sender.ShouldBe(clientAddress);
                });
            };

            Enumerable.Range(0, 20).ForEach(_ => checkSendingToServer());
            Enumerable.Range(0, 20).ForEach(_ => checkSendingToClient());
            Enumerable.Range(0, 20).ForEach(i =>
            {
                if (i%2 == 0) checkSendingToServer();
                else checkSendingToClient();
            });
        }

        [Fact]
        public void The_UDP_Fire_and_Forget_implementation_must_call_SocketOption_beforeBind_method_before_bind()
        {
            var commander = CreateTestProbe();
            var assertOption = new AssertBeforeBind();
            commander.Send(Udp.Instance.Apply(Sys).Manager, new Udp.Bind(TestActor, _addresses[2], options: new[] {assertOption}));
            commander.ExpectMsg<Udp.Bound>(x => x.LocalAddress.ShouldBe(_addresses[2]));
            Assert.Equal(1, assertOption.BeforeCalled);
        }

        [Fact]
        public void The_UDP_Fire_and_Forget_implementation_must_call_SocketOption_afterConnect_method_after_binding()
        {
            var commander = CreateTestProbe();
            var assertOption = new AssertAfterChannelBind();
            commander.Send(Udp.Instance.Apply(Sys).Manager, new Udp.Bind(TestActor, _addresses[3], options: new[] { assertOption }));
            commander.ExpectMsg<Udp.Bound>(x => x.LocalAddress.ShouldBe(_addresses[3]));
            Assert.Equal(1, assertOption.AfterCalled);
        }

        [Fact]
        public void The_UDP_Fire_and_Forget_implementation_must_call_DatagramChannelCreator_create_method_when_opening_channel()
        {
            var commander = CreateTestProbe();
            var assertOption = new AssertOpenDatagramChannel();
            commander.Send(Udp.Instance.Apply(Sys).Manager, new Udp.Bind(TestActor, _addresses[4], options: new[] { assertOption }));
            commander.ExpectMsg<Udp.Bound>(x => x.LocalAddress.ShouldBe(_addresses[4]));
            Assert.Equal(1, assertOption.OpenCalled);
        }


        class AssertBeforeBind : Inet.SocketOption
        {
            public int BeforeCalled { get; private set; }

            public override void BeforeDatagramBind(Socket ds)
            {
                Assert.True(!ds.IsBound);
                BeforeCalled += 1;
            }
        }

        class AssertAfterChannelBind : Inet.SocketOptionV2
        {
            public int AfterCalled { get; set; }
            public override void AfterBind(Socket s)
            {
                Assert.True(s.IsBound);
                AfterCalled += 1;
            }
        }

        class AssertOpenDatagramChannel : Inet.DatagramChannelCreator
        {
            public int OpenCalled { get; set; }

            public override DatagramChannel Create()
            {
                OpenCalled += 1;
                return base.Create();
            }
        }
    }
}
