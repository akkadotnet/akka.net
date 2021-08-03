//-----------------------------------------------------------------------
// <copyright file="UdpListenerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Akka.Actor;
using Akka.IO;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using UdpListener = Akka.IO.UdpListener;

namespace Akka.Tests.IO
{
    public class UdpListenerSpec : AkkaSpec
    {
        public UdpListenerSpec(ITestOutputHelper output)
            : base(@"
                    akka.actor.serialize-creators = on
                    akka.actor.serialize-messages = on
                    akka.io.udp.max-channels = unlimited
                    akka.io.udp.nr-of-selectors = 1
                    akka.io.udp.direct-buffer-pool-limit = 100
                    akka.io.udp.direct-buffer-size = 1024", output)
        { }

        [Fact]
        public void A_UDP_Listener_must_let_the_bind_commander_know_when_binding_is_complete()
        {
            new TestSetup(this).Run(x =>
            {
                x.BindCommander.ExpectMsg<Udp.Bound>();
            });           
        }

        [Fact]
        public void A_UDP_Listener_must_forward_incoming_packets_to_handler_actor()
        {
            const string dgram = "Fly little packet!";
            new TestSetup(this).Run(x =>
            {
                x.BindCommander.ExpectMsg<Udp.Bound>();
                x.SendDataToLocal(Encoding.UTF8.GetBytes(dgram));
                x.Handler.ExpectMsg<Udp.Received>(_ => Assert.Equal(dgram, Encoding.UTF8.GetString(_.Data.ToArray())));
                x.SendDataToLocal(Encoding.UTF8.GetBytes(dgram));
                x.Handler.ExpectMsg<Udp.Received>(_ => Assert.Equal(dgram, Encoding.UTF8.GetString(_.Data.ToArray())));
            });           
        }
        
        [Fact]
        public void A_UDP_Listener_must_be_able_to_send_and_receive_when_server_goes_away()
        {
            new TestSetup(this).Run(x =>
            {
                x.BindCommander.ExpectMsg<Udp.Bound>();
                
                // Receive UDP messages from a sender
                const string requestMessage = "This is my last request!";
                var notExistingEndPoint = x.SendDataToLocal(Encoding.UTF8.GetBytes(requestMessage));
                x.Handler.ExpectMsg<Udp.Received>(_ =>
                {
                    Assert.Equal(requestMessage, Encoding.UTF8.GetString(_.Data.ToArray()));
                });

                // Try to reply to this sender which DOES NOT EXIST any more
                // Important: The UDP port of the reply must match the listing UDP port!
                // This UDP port will also be used for ICMP error reporting.
                // Hint: On Linux the listener port cannot be reused. We are using the udp actor to respond.
                IActorRef localSender = x.Listener;
                const string response = "Are you still alive?"; // he is not
                localSender.Tell(Udp.Send.Create(ByteString.FromBytes(Encoding.UTF8.GetBytes(response)), notExistingEndPoint));

                // Now an ICMP error message "port unreachable" (SocketError.ConnectionReset) is sent to our UDP server port
                x.Handler.ExpectNoMsg(TimeSpan.FromSeconds(1));

                const string followUpMessage = "Back online!";
                x.SendDataToLocal(Encoding.UTF8.GetBytes(followUpMessage));
                x.Handler.ExpectMsg<Udp.Received>(_ => Assert.Equal(followUpMessage, Encoding.UTF8.GetString(_.Data.ToArray())));
            });         
        }

        class TestSetup
        {
            private readonly TestKitBase _kit;

            private readonly TestProbe _handler;
            private readonly TestProbe _bindCommander;
            private readonly TestProbe _parent;
            private readonly IPEndPoint _localEndpoint;
            private readonly TestActorRef<ListenerParent> _parentRef;

            public TestSetup(TestKitBase kit) :
                this(kit, TestUtils.TemporaryServerAddress())
            {

            }

            public TestSetup(TestKitBase kit, IPEndPoint localEndpoint)
            {
                _kit = kit;
                _localEndpoint = localEndpoint;
                _handler = kit.CreateTestProbe();
                _bindCommander = kit.CreateTestProbe();
                _parent = kit.CreateTestProbe();
                _parentRef = new TestActorRef<ListenerParent>(kit.Sys, Props.Create(() => new ListenerParent(this)));
            }

            public void Run(Action<TestSetup> test)
            {
                test(this);
            }

            public void BindListener()
            {
                _bindCommander.ExpectMsg<Udp.Bound>();
            }

            public IPEndPoint SendDataToLocal(byte[] buffer)
            {
                return SendDataToEndpoint(buffer, _localEndpoint);
            }

            public IPEndPoint SendDataToEndpoint(byte[] buffer, IPEndPoint receiverEndpoint)
            {
                var tempEndpoint = TestUtils.TemporaryServerAddress();
                return SendDataToEndpoint(buffer, receiverEndpoint, tempEndpoint);
            }

            public IPEndPoint SendDataToEndpoint(byte[] buffer, IPEndPoint receiverEndpoint, IPEndPoint senderEndpoint)
            {
                using (var udpClient = new UdpClient(senderEndpoint.Port))
                {
                    udpClient.Connect(receiverEndpoint);
                    udpClient.Send(buffer, buffer.Length);
                }
                return senderEndpoint;
            }
            
            public IActorRef Listener { get { return _parentRef.UnderlyingActor.Listener; } }
            
            public TestProbe BindCommander { get { return _bindCommander; } }

            public TestProbe Handler { get { return _handler; } }
            
            public IPEndPoint LocalLocalEndPoint { get { return _localEndpoint; }  }

            class ListenerParent : ActorBase
            {
                private readonly TestSetup _test;
                private readonly IActorRef _listener;
                public ListenerParent(TestSetup test)
                {
                    _test = test;

                    _listener = Context.ActorOf(Props.Create(() =>
                        new UdpListener(
                            Udp.Instance.Apply(Context.System),
                            test._bindCommander.Ref,
                            new Udp.Bind(_test._handler.Ref, test._localEndpoint, new Inet.SocketOption[]{})))
                                                              .WithDeploy(Deploy.Local));
                    _test._parent.Watch(_listener);
                }

                internal IActorRef Listener { get { return _listener; } }

                protected override bool Receive(object message)
                {
                    _test._parent.Forward(message);
                    return true;
                }

                protected override SupervisorStrategy SupervisorStrategy()
                {
                    return Akka.Actor.SupervisorStrategy.StoppingStrategy;
                }

            }
        }
    }
}
