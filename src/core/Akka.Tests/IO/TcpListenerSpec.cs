//-----------------------------------------------------------------------
// <copyright file="TcpListenerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using TcpListener = Akka.IO.TcpListener;

namespace Akka.Tests.IO
{
    public class TcpListenerSpec : AkkaSpec
    {
        public TcpListenerSpec(ITestOutputHelper output)
            : base("""

                                        akka.actor.serialize-creators = on
                                        akka.actor.serialize-messages = on
                                        akka.io.tcp.register-timeout = 500ms
                                        akka.io.tcp.max-received-message-size = 1024
                                        akka.io.tcp.direct-buffer-size = 512
                                        akka.actor.serialize-creators = on
                                        akka.io.tcp.batch-accept-limit = 2
                   """, output)
        {
        }

        [Fact]
        public async Task A_TCP_Listener_must_let_the_bind_commander_know_when_binding_is_complete()
        {
            await new TestSetup(this, pullMode: false).RunAsync(async x =>
            {
                await x.BindCommander.ExpectMsgAsync<Tcp.Bound>();
            });
        }

        [Fact]
        public async Task A_TCP_Listener_must_continue_to_accept_connections_after_a_previous_accept()
        {
            await new TestSetup(this, pullMode: false).RunAsync(async x =>
            {
                await x.BindListener();

                await x.AttemptConnectionToEndpoint();
                await x.AttemptConnectionToEndpoint();
            });
        }

        [Fact]
        public async Task A_TCP_Listener_must_provide_metrics()
        {
            await new TestSetup(this, pullMode: false).RunAsync(async x =>
            {
                await x.BindListener();

                var socket = await x.AttemptConnectionToEndpoint();
                await x.Handler.ExpectMsgAsync<Tcp.Connected>();

                var probe = CreateTestProbe();
                x.Listener.Tell(new Tcp.SubscribeToTcpListenerStats(probe.Ref));
                var metrics = await probe.ExpectMsgAsync<Tcp.TcpListenerStatistics>();

                Assert.Equal(1, metrics.AcceptedIncomingConnections);
                //
                // // close the socket
                // socket.Close();
                // socket.Dispose();
                //
                // // wait for the connection to be closed
                // await x.Handler.ExpectMsgAsync<Tcp.ConnectionClosed>();
                //
                // // force the listener to publish stats
                // x.Listener.Tell(TcpListener.PublishStats.Instance);
                // metrics = await probe.ExpectMsgAsync<Tcp.TcpListenerStatistics>();
                // Assert.Equal(1, metrics.AcceptedIncomingConnections);
                // Assert.Equal(1, metrics.IncomingConnectionsClosed);
            });
        }

        [Fact]
        public async Task A_TCP_Listener_must_react_to_unbind_commands_by_replying_with_unbound_and_stopping_itself()
        {
            await new TestSetup(this, pullMode: false).RunAsync(async x =>
            {
                await x.BindListener();

                var unbindCommander = CreateTestProbe();
                unbindCommander.Send(x.Listener, Tcp.Unbind.Instance);

                await unbindCommander.ExpectMsgAsync(Tcp.Unbound.Instance);
                await x.Parent.ExpectTerminatedAsync(x.Listener);
            });
        }

        private class TestSetup
        {
            private readonly TestKitBase _kit;

            private readonly TestProbe _handler;
            private readonly IActorRef _handlerRef;
            private readonly TestActorRef<ListenerParent> _parentRef;

            public TestSetup(TestKitBase kit, bool pullMode)
            {
                _kit = kit;

                _handler = kit.CreateTestProbe();
                _handlerRef = _handler.Ref;
                BindCommander = kit.CreateTestProbe();
                Parent = kit.CreateTestProbe();
                SelectorRouter = kit.CreateTestProbe();

                _parentRef =
                    new TestActorRef<ListenerParent>(kit.Sys, Props.Create(() => new ListenerParent(this, pullMode)));
            }

            public void Run(Action<TestSetup> test)
            {
                test(this);
            }

            public async Task RunAsync(Func<TestSetup, Task> test)
            {
                await test(this);
            }

            public async Task BindListener()
            {
                var bound = await BindCommander.ExpectMsgAsync<Tcp.Bound>();
                LocalEndPoint = (IPEndPoint)bound.LocalAddress;
            }

            public async Task<Socket> AttemptConnectionToEndpoint()
            {
                var s = new Socket(LocalEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                await s
                    .ConnectAsync(LocalEndPoint);
                return s;
            }

            public IActorRef Listener
            {
                get { return _parentRef.UnderlyingActor.Listener; }
            }

            public TestProbe SelectorRouter { get; }

            public TestProbe BindCommander { get; }
            public TestProbe Parent { get; }

            public TestProbe Handler => _handler;

            public IPEndPoint LocalEndPoint { get; private set; }

            internal void AfterBind(Socket socket)
                => LocalEndPoint = (IPEndPoint)socket.LocalEndPoint;

            private class ListenerParent : ActorBase
            {
                private readonly TestSetup _test;
                private readonly bool _pullMode;
                private readonly IActorRef _listener;

                public ListenerParent(TestSetup test, bool pullMode)
                {
                    _test = test;
                    _pullMode = pullMode;

                    var endpoint = new IPEndPoint(IPAddress.Loopback, 0);

                    _listener = Context.ActorOf(Props.Create(() =>
                            new TcpListener(
                                Tcp.For(Context.System),
                                test.BindCommander.Ref,
                                new Tcp.Bind(
                                    _test._handler.Ref,
                                    endpoint,
                                    100,
                                    new Inet.SocketOption[] { new TestSocketOption(socket => _test.AfterBind(socket)) },
                                    pullMode)))
                        .WithDeploy(Deploy.Local));

                    _test.Parent.Watch(_listener);
                }

                internal IActorRef Listener
                {
                    get { return _listener; }
                }

                protected override bool Receive(object message)
                {
                    _test.Parent.Forward(message);
                    return true;
                }

                protected override SupervisorStrategy SupervisorStrategy()
                {
                    return Akka.Actor.SupervisorStrategy.StoppingStrategy;
                }

                private class TestSocketOption : Inet.SocketOptionV2
                {
                    private readonly Action<Socket> _afterBindCallback;

                    public TestSocketOption(Action<Socket> afterBindCallback)
                    {
                        _afterBindCallback = afterBindCallback;
                    }

                    public override void AfterBind(Socket s)
                        => _afterBindCallback(s);
                }
            }
        }
    }
}