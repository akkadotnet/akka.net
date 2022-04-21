//-----------------------------------------------------------------------
// <copyright file="UdpIntegrationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Akka.Actor;
using Akka.IO;
using Akka.IO.Buffers;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;
using FluentAssertions.Extensions;

namespace Akka.Tests.IO
{
    public class UdpIntegrationSpec : AkkaSpec
    {
        public UdpIntegrationSpec(ITestOutputHelper output)
            : base(@"
                    akka.actor.serialize-creators = on
                    akka.actor.serialize-messages = on
                    akka.io.udp.max-channels = unlimited
                    akka.io.udp.nr-of-selectors = 1

                    akka.io.udp.buffer-pool = ""akka.io.udp.direct-buffer-pool""
                    akka.io.udp.nr-of-selectors = 1
                    # This comes out to be about 1.6 Mib maximum total buffer size
                    akka.io.udp.direct-buffer-pool.buffer-size = 512
                    akka.io.udp.direct-buffer-pool.buffers-per-segment = 32
                    akka.io.udp.direct-buffer-pool.buffer-pool-limit = 100
                    # akka.io.udp.trace-logging = true
                    akka.loglevel = DEBUG", output)
        {
        }

        private (IActorRef, IPEndPoint) BindUdp(IActorRef handler)
        {
            var commander = CreateTestProbe();
            commander.Send(Sys.Udp(), new Udp.Bind(handler, new IPEndPoint(IPAddress.Loopback, 0)));
            IPEndPoint localEndpoint = null;
            commander.ExpectMsg<Udp.Bound>(x => localEndpoint = (IPEndPoint)x.LocalAddress);
            return (commander.Sender, localEndpoint);
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
            var (_, localEndpoint) = BindUdp(TestActor);
            var data = ByteString.FromString("To infinity and beyond!");
            SimpleSender().Tell(Udp.Send.Create(data, localEndpoint));

            ExpectMsg<Udp.Received>(x => x.Data.ShouldBe(data));
        }

        [Fact]
        public void The_UDP_Fire_and_Forget_implementation_must_be_able_to_send_multipart_ByteString_without_binding()
        {
            var (_, localEndpoint) = BindUdp(TestActor);
            var data = ByteString.FromString("This ") 
                + ByteString.FromString("is ") 
                + ByteString.FromString("multiline ") 
                + ByteString.FromString(" string!");
            SimpleSender().Tell(Udp.Send.Create(data, localEndpoint));

            ExpectMsg<Udp.Received>(x => x.Data.ShouldBe(data));
        }

        [Fact]
        public void BugFix_UDP_fire_and_forget_must_handle_batch_writes_when_bound()
        {
            var (server, serverLocalEndpoint) = BindUdp(TestActor);
            var (client, clientLocalEndpoint) = BindUdp(TestActor);
            var data = ByteString.FromString("Fly little packet!");

            // queue 3 writes
            client.Tell(Udp.Send.Create(data, serverLocalEndpoint));
            client.Tell(Udp.Send.Create(data, serverLocalEndpoint));
            client.Tell(Udp.Send.Create(data, serverLocalEndpoint));

            var raw = ReceiveN(3);
            var msgs = raw.Cast<Udp.Received>();
            msgs.Sum(x => x.Data.Count).Should().Be(data.Count*3);
            ExpectNoMsg(100.Milliseconds()); 

            // repeat in the other direction
            server.Tell(Udp.Send.Create(data, clientLocalEndpoint));
            server.Tell(Udp.Send.Create(data, clientLocalEndpoint));
            server.Tell(Udp.Send.Create(data, clientLocalEndpoint));

            raw = ReceiveN(3);
            msgs = raw.Cast<Udp.Received>();
            msgs.Sum(x => x.Data.Count).Should().Be(data.Count * 3);
        }

        [Fact]
        public void The_UDP_Fire_and_Forget_implementation_must_be_able_to_send_several_packet_back_and_forth_with_binding()
        {
            var serverProbe = CreateTestProbe();
            var clientProbe = CreateTestProbe();
            var (server, serverLocalEndpoint) = BindUdp(serverProbe);
            var (client, clientLocalEndpoint) = BindUdp(clientProbe);

            void CheckSendingToClient(int iteration)
            {
                server.Tell(Udp.Send.Create(ByteString.FromString(iteration.ToString()), clientLocalEndpoint));
                clientProbe.ExpectMsg<Udp.Received>(x =>
                {
                    x.Data.ToString().ShouldBe(iteration.ToString());
                    x.Sender.Is(serverLocalEndpoint).ShouldBeTrue($"Client sender {x.Sender} was expected to be {serverLocalEndpoint}");
                }, hint: $"sending to client failed in {iteration} iteration");
            }

            void CheckSendingToServer(int iteration)
            {
                client.Tell(Udp.Send.Create(ByteString.FromString(iteration.ToString()), serverLocalEndpoint));
                serverProbe.ExpectMsg<Udp.Received>(x =>
                {
                    x.Data.ToString().ShouldBe(iteration.ToString());
                    x.Sender.Is(clientLocalEndpoint).ShouldBeTrue($"Server sender {x.Sender} was expected to be {clientLocalEndpoint}");
                }, hint: $"sending to client failed in {iteration} iteration");
            }

            const int iterations = 20;
            for (int i = 1; i <= iterations; i++) CheckSendingToServer(i);
            for (int i = 1; i <= iterations; i++) CheckSendingToClient(i);
            for (int i = 1; i <= iterations; i++)
            {
                if (i % 2 == 0) CheckSendingToServer(i);
                else CheckSendingToClient(i);
            }
        }

        [Fact]
        public void The_UDP_Fire_and_Forget_implementation_must_be_able_to_send_several_packets_in_a_row()
        {
            var (server, serverLocalEndpoint) = BindUdp(TestActor);
            var (client, clientLocalEndpoint) = BindUdp(TestActor);

            void CheckSendingToClient(ByteString expected)
            {
                ExpectMsg<Udp.Received>(x =>
                {
                    x.Data.ShouldBe(expected);
                    x.Sender.Is(serverLocalEndpoint).ShouldBeTrue($"{x.Sender} was expected to be {serverLocalEndpoint}");
                });
            }

            void CheckSendingToServer(ByteString expected)
            {
                ExpectMsg<Udp.Received>(x =>
                {
                    x.Data.ShouldBe(expected);
                    x.Sender.Is(clientLocalEndpoint).ShouldBeTrue($"{x.Sender} was expected to be {clientLocalEndpoint}");
                });
            }

            var data = new[]
            {
                ByteString.FromString("a"),
                ByteString.FromString("bb"),
                ByteString.FromString("ccc"),
                ByteString.FromString("dddd"),
                ByteString.FromString("eeeee"),
                ByteString.FromString("ffffff"),
                ByteString.FromString("ggggggg"),
                ByteString.FromString("hhhhhhhh"),
                ByteString.FromString("iiiiiiiii"),
                ByteString.FromString("jjjjjjjjjj")
            };

            var iterations = data.Length;
            for (int i = 0; i < iterations; i++) client.Tell(Udp.Send.Create(data[i], serverLocalEndpoint));
            for (int i = 0; i < iterations; i++) CheckSendingToServer(data[i]);

            for (int i = 0; i < iterations; i++) server.Tell(Udp.Send.Create(data[i], clientLocalEndpoint));
            for (int i = 0; i < iterations; i++) CheckSendingToClient(data[i]);
        }

        [Fact]
        public void The_UDP_Fire_and_Forget_implementation_must_not_leak_memory()
        {
            const int batchCount = 2000;
            const int batchSize = 100;
            
            var udp = Udp.Instance.Apply(Sys);
            var poolInfo = udp.SocketEventArgsPool.BufferPoolInfo;
            poolInfo.Type.Should().Be(typeof(DirectBufferPool));
            poolInfo.Free.Should().Be(poolInfo.TotalSize);
            poolInfo.Used.Should().Be(0);
            
            var serverProbe = CreateTestProbe();
            var (server, _) = BindUdp(serverProbe);
            var clientProbe = CreateTestProbe();
            var (client, clientLocalEndpoint) = BindUdp(clientProbe);
            
            var data = ByteString.FromString("Fly little packet!");

            // send a lot of packets through, the byte buffer pool should not leak anything
            for (var n = 0; n < batchCount; ++n)
            {
                for (var i = 0; i < batchSize; i++) 
                    server.Tell(Udp.Send.Create(data, clientLocalEndpoint));

                var msgs = clientProbe.ReceiveN(batchSize);
                var receives = msgs.Cast<Udp.Received>();
                receives.Sum(r => r.Data.Count).Should().Be(data.Count * batchSize);
            }
            
            // stop all connections so all receives are stopped and all pending SocketAsyncEventArgs are collected
            server.Tell(Udp.Unbind.Instance, serverProbe);
            serverProbe.ExpectMsg<Udp.Unbound>();
            client.Tell(Udp.Unbind.Instance, clientProbe);
            clientProbe.ExpectMsg<Udp.Unbound>();
            
            // wait for all SocketAsyncEventArgs to be released
            Thread.Sleep(1000);
            
            poolInfo = udp.SocketEventArgsPool.BufferPoolInfo;
            poolInfo.Type.Should().Be(typeof(DirectBufferPool));
            poolInfo.Free.Should().Be(poolInfo.TotalSize);
            poolInfo.Used.Should().Be(0);
        }
        
        [Fact]
        public void The_UDP_Fire_and_Forget_SimpleSender_implementation_must_not_leak_memory()
        {
            const int batchCount = 2000;
            const int batchSize = 100;
            
            var udp = Udp.Instance.Apply(Sys);
            var poolInfo = udp.SocketEventArgsPool.BufferPoolInfo;
            poolInfo.Type.Should().Be(typeof(DirectBufferPool));
            poolInfo.Free.Should().Be(poolInfo.TotalSize);
            poolInfo.Used.Should().Be(0);
            
            var serverProbe = CreateTestProbe();
            var (server, serverLocalEndpoint) = BindUdp(serverProbe);
            var sender = SimpleSender();
            
            var data = ByteString.FromString("Fly little packet!");

            // send a lot of packets through, the byte buffer pool should not leak anything
            for (var n = 0; n < batchCount; ++n)
            {
                for (int i = 0; i < batchSize; i++) 
                    sender.Tell(Udp.Send.Create(data, serverLocalEndpoint));

                var msgs = serverProbe.ReceiveN(batchSize);
                var receives = msgs.Cast<Udp.Received>();
                receives.Sum(r => r.Data.Count).Should().Be(data.Count * batchSize);
            }
            
            // stop all connections so all receives are stopped and all pending SocketAsyncEventArgs are collected
            server.Tell(Udp.Unbind.Instance, serverProbe);
            serverProbe.ExpectMsg<Udp.Unbound>();
            
            // wait for all SocketAsyncEventArgs to be released
            Thread.Sleep(1000);
            
            poolInfo = udp.SocketEventArgsPool.BufferPoolInfo;
            poolInfo.Type.Should().Be(typeof(DirectBufferPool));
            poolInfo.Free.Should().Be(poolInfo.TotalSize);
            poolInfo.Used.Should().Be(0);
        }
        
        [Fact]
        public void The_UDP_Fire_and_Forget_implementation_must_call_SocketOption_beforeBind_method_before_bind()
        {
            var commander = CreateTestProbe();
            var assertOption = new AssertBeforeBind();
            commander.Send(
                Udp.Instance.Apply(Sys).Manager, 
                new Udp.Bind(TestActor, new IPEndPoint(IPAddress.Loopback, 0), options: new[] {assertOption}));
            commander.ExpectMsg<Udp.Bound>();
            Assert.Equal(1, assertOption.BeforeCalled);
        }

        [Fact]
        public void The_UDP_Fire_and_Forget_implementation_must_call_SocketOption_afterConnect_method_after_binding()
        {
            var commander = CreateTestProbe();
            var assertOption = new AssertAfterChannelBind();
            commander.Send(
                Udp.Instance.Apply(Sys).Manager,
                new Udp.Bind(TestActor, new IPEndPoint(IPAddress.Loopback, 0), options: new[] { assertOption }));
            commander.ExpectMsg<Udp.Bound>();
            Assert.Equal(1, assertOption.AfterCalled);
        }

        [Fact]
        public void The_UDP_Fire_and_Forget_implementation_must_call_DatagramChannelCreator_create_method_when_opening_channel()
        {
            var commander = CreateTestProbe();
            var assertOption = new AssertOpenDatagramChannel();
            commander.Send(
                Udp.Instance.Apply(Sys).Manager,
                new Udp.Bind(
                    TestActor, 
                    new IPEndPoint(IPAddress.Loopback, 0), 
                    options: new[] { assertOption }));
            commander.ExpectMsg<Udp.Bound>();
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


            public override Socket Create()
            {
                OpenCalled += 1;
                return base.Create();
            }

            public override Socket Create(AddressFamily addressFamily)
            {
                OpenCalled += 1;
                return base.Create(addressFamily);
            }
        }
    }
}
