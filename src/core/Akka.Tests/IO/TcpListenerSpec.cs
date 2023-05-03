//-----------------------------------------------------------------------
// <copyright file="TcpListenerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using TcpListener = Akka.IO.TcpListener;

namespace Akka.Tests.IO
{
    public class TcpListenerSpec : AkkaSpec
    {
        public TcpListenerSpec()
            : base(@"
                     akka.actor.serialize-creators = on
                     akka.actor.serialize-messages = on
                     akka.io.tcp.register-timeout = 500ms
                     akka.io.tcp.max-received-message-size = 1024
                     akka.io.tcp.direct-buffer-size = 512
                     akka.actor.serialize-creators = on
                     akka.io.tcp.batch-accept-limit = 2")
        { }

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
        public async Task A_TCP_Listener_must_react_to_unbind_commands_by_replying_with_unbound_and_stopping_itself()
        {
            await new TestSetup(this, pullMode:false).RunAsync(async x =>
            {
                await x.BindListener();

                var unbindCommander = CreateTestProbe();
                unbindCommander.Send(x.Listener, Tcp.Unbind.Instance);

                await unbindCommander.ExpectMsgAsync(Tcp.Unbound.Instance);
                await x.Parent.ExpectTerminatedAsync(x.Listener);
            });    
        }

        class TestSetup
        {
            private readonly TestKitBase _kit;
            private readonly bool _pullMode;

            private readonly TestProbe _handler;
            private readonly IActorRef _handlerRef;
            private readonly TestActorRef<ListenerParent> _parentRef;
            
            public TestSetup(TestKitBase kit, bool pullMode)
            {
                _kit = kit;
                _pullMode = pullMode;

                _handler = kit.CreateTestProbe();
                _handlerRef = _handler.Ref;
                BindCommander = kit.CreateTestProbe();
                Parent = kit.CreateTestProbe();
                SelectorRouter = kit.CreateTestProbe();

                _parentRef = new TestActorRef<ListenerParent>(kit.Sys, Props.Create(() => new ListenerParent(this, pullMode)));
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

            public async Task AttemptConnectionToEndpoint()
            {
                await new Socket(LocalEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                    .ConnectAsync(LocalEndPoint);
            }

            public IActorRef Listener { get { return _parentRef.UnderlyingActor.Listener; } }

            public TestProbe SelectorRouter { get; }

            public TestProbe BindCommander { get; }
            public TestProbe Parent { get; }

            public IPEndPoint LocalEndPoint { get; private set; }

            internal void AfterBind(Socket socket)
                => LocalEndPoint = (IPEndPoint)socket.LocalEndPoint;

            class ListenerParent : ActorBase
            {
                private readonly TestSetup _test;
                private readonly bool _pullMode;

                public ListenerParent(TestSetup test, bool pullMode)
                {
                    _test = test;
                    _pullMode = pullMode;

                    var endpoint = new IPEndPoint(IPAddress.Loopback, 0);

                    Listener = Context.ActorOf(Props.Create(() =>
                        new TcpListener(
                            Tcp.Instance.Apply(Context.System),
                            test.BindCommander.Ref,
                            new Tcp.Bind(
                                _test._handler.Ref, 
                                endpoint, 
                                100, 
                                new Inet.SocketOption[]{ new TestSocketOption(socket => _test.AfterBind(socket)) }, 
                                pullMode)))
                        .WithDeploy(Deploy.Local));
                    
                    _test.Parent.Watch(Listener);
                }

                internal IActorRef Listener { get; }

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
