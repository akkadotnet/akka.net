//-----------------------------------------------------------------------
// <copyright file="TcpListenerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
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
        public void A_TCP_Listener_must_let_the_bind_commander_know_when_binding_is_complete()
        {
            new TestSetup(this, pullMode: false).Run(x =>
            {
                x.BindCommander.ExpectMsg<Tcp.Bound>();
            });           
        }

        [Fact]
        public void A_TCP_Listener_must_continue_to_accept_connections_after_a_previous_accept()
        {
            new TestSetup(this, pullMode: false).Run(x =>
            {
                x.BindListener();

                x.AttemptConnectionToEndpoint();
                x.AttemptConnectionToEndpoint();
            });
        }

        [Fact]
        public void A_TCP_Listener_must_react_to_unbind_commands_by_replying_with_unbound_and_stopping_itself()
        {
            new TestSetup(this, pullMode:false).Run(x =>
            {
                x.BindListener();

                var unbindCommander = CreateTestProbe();
                unbindCommander.Send(x.Listener, Tcp.Unbind.Instance);

                unbindCommander.ExpectMsg(Tcp.Unbound.Instance);
                x.Parent.ExpectTerminated(x.Listener);
            });    
        }

        class TestSetup
        {
            private readonly TestKitBase _kit;
            private readonly bool _pullMode;

            private readonly TestProbe _handler;
            private readonly IActorRef _handlerRef;
            private readonly TestProbe _bindCommander;
            private readonly TestProbe _parent;
            private readonly TestProbe _selectorRouter;
            private readonly TestActorRef<ListenerParent> _parentRef;
            
            public TestSetup(TestKitBase kit, bool pullMode)
            {
                _kit = kit;
                _pullMode = pullMode;

                _handler = kit.CreateTestProbe();
                _handlerRef = _handler.Ref;
                _bindCommander = kit.CreateTestProbe();
                _parent = kit.CreateTestProbe();
                _selectorRouter = kit.CreateTestProbe();

                _parentRef = new TestActorRef<ListenerParent>(kit.Sys, Props.Create(() => new ListenerParent(this, pullMode)));
            }

            public void Run(Action<TestSetup> test)
            {
                test(this);
            }

            public void BindListener()
            {
                var bound = _bindCommander.ExpectMsg<Tcp.Bound>();
                LocalEndPoint = (IPEndPoint)bound.LocalAddress;
            }

            public void AttemptConnectionToEndpoint()
            {
                new Socket(LocalEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                    .Connect(LocalEndPoint);
            }

            public IActorRef Listener { get { return _parentRef.UnderlyingActor.Listener; } }

            public TestProbe SelectorRouter
            {
                get { return _selectorRouter; }
            }

            public TestProbe BindCommander { get { return _bindCommander; } }
            public TestProbe Parent { get { return _parent; } }

            public IPEndPoint LocalEndPoint { get; private set; }

            internal void AfterBind(Socket socket)
                => LocalEndPoint = (IPEndPoint)socket.LocalEndPoint;

            class ListenerParent : ActorBase
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
                            Tcp.Instance.Apply(Context.System),
                            test._bindCommander.Ref,
                            new Tcp.Bind(
                                _test._handler.Ref, 
                                endpoint, 
                                100, 
                                new Inet.SocketOption[]{ new TestSocketOption(socket => _test.AfterBind(socket)) }, 
                                pullMode)))
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
