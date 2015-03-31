//-----------------------------------------------------------------------
// <copyright file="EventBusSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
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
            : base(@"akka.io.tcp.register-timeout = 500ms
                     akka.io.tcp.max-received-message-size = 1024
                     akka.io.tcp.direct-buffer-size = 512
                     akka.actor.serialize-creators = on
                     akka.io.tcp.batch-accept-limit = 2
                    ")
        { }

        [Fact]
        public void A_TCP_Listner_must_register_its_server_socket_channel_with_its_selector()
        {
            new TestSetup(this, pullMode: false).Run(x => { });
        }

        [Fact]
        public void A_TCP_Listner_must_let_the_bind_commander_know_when_binding_is_complete()
        {
            new TestSetup(this, pullMode: false).Run(x =>
            {
                x.Listner.Tell(new ChannelRegistration(
                    o => { }, 
                    o => { }));

                x.BindCommander.ExpectMsg<Tcp.Bound>();
            });           
        }

        [Fact]
        public void A_TCP_Listner_must_accept_acceptable_connection_and_register_them_with_its_parent()
        {
            new TestSetup(this, pullMode: false).Run(x =>
            {
                x.BindListener();
   
                x.AttemptConnectionToEndpoint();
                x.AttemptConnectionToEndpoint();
                x.AttemptConnectionToEndpoint();

                // since the batch-accept-limit is 2 we must only receive 2 accepted connections
                x.Listner.Tell(SelectionHandler.ChannelAcceptable.Instance);

                x.ExpectWorkerForCommand();
                x.ExpectWorkerForCommand();
                x.SelectorRouter.ExpectNoMsg(100);
                x.InterestCallReceiver.ExpectMsg((int) SocketAsyncOperation.Accept);

                // and pick up the last remaining connection on the next ChannelAcceptable
                x.Listner.Tell(SelectionHandler.ChannelAcceptable.Instance);
                x.ExpectWorkerForCommand();
            });
        }

        [Fact]
        public void A_TCP_Listner_must_continue_to_accept_connections_after_a_previous_accept()
        {
            new TestSetup(this, pullMode: false).Run(x =>
            {
                x.BindListener();

                x.AttemptConnectionToEndpoint();
                x.Listner.Tell(SelectionHandler.ChannelAcceptable.Instance);
                x.ExpectWorkerForCommand();
                x.SelectorRouter.ExpectNoMsg(100);
                x.InterestCallReceiver.ExpectMsg((int) SocketAsyncOperation.Accept);

                x.AttemptConnectionToEndpoint();
                x.Listner.Tell(SelectionHandler.ChannelAcceptable.Instance);
                x.ExpectWorkerForCommand();
                x.SelectorRouter.ExpectNoMsg(100);
                x.InterestCallReceiver.ExpectMsg((int) SocketAsyncOperation.Accept);
            });
        }

        [Fact]
        public void A_TCP_Listner_must_not_accept_connections_after_a_previous_accept_unit_read_is_reenabled()
        {
            new TestSetup(this, pullMode: true).Run(x =>
            {
                x.BindListener();

                x.AttemptConnectionToEndpoint();
                ExpectNoMsg(100);

                x.Listner.Tell(new Tcp.ResumeAccepting(batchSize: 1));
                x.Listner.Tell(SelectionHandler.ChannelAcceptable.Instance);
                x.ExpectWorkerForCommand();
                x.SelectorRouter.ExpectNoMsg(100);
                x.InterestCallReceiver.ExpectMsg((int) SocketAsyncOperation.Accept);

                // No more accepts are allowed now
                x.InterestCallReceiver.ExpectNoMsg(100);

                x.Listner.Tell(new Tcp.ResumeAccepting(batchSize: 2));
                x.InterestCallReceiver.ExpectMsg((int) SocketAsyncOperation.Accept);

                x.AttemptConnectionToEndpoint();
                x.Listner.Tell(SelectionHandler.ChannelAcceptable.Instance);
                x.ExpectWorkerForCommand();
                x.SelectorRouter.ExpectNoMsg(100);
                // There is still one token remaining, accepting
                x.InterestCallReceiver.ExpectMsg((int)SocketAsyncOperation.Accept);

                x.AttemptConnectionToEndpoint();
                x.Listner.Tell(SelectionHandler.ChannelAcceptable.Instance);
                x.ExpectWorkerForCommand();
                x.SelectorRouter.ExpectNoMsg(100);

                // Tokens are depleted now
                x.InterestCallReceiver.ExpectNoMsg(100);
            });
        }

        [Fact]
        public void A_TCP_Listner_must_react_to_unbind_commands_by_replying_with_unbound_and_stopping_itself()
        {
            new TestSetup(this, pullMode:false).Run(x =>
            {
                x.BindListener();

                var unbindCommander = CreateTestProbe();
                unbindCommander.Send(x.Listner, Tcp.Unbind.Instance);

                unbindCommander.ExpectMsg(Tcp.Unbound.Instance);
                x.Parent.ExpectTerminated(x.Listner);
            });    
        }

        [Fact]
        public void A_TCP_Listner_must_drop_an_incoming_connection_if_it_cannot_be_registered_with_a_selector()
        {
            new TestSetup(this, pullMode: false).Run(x =>
            {
                x.BindListener();

                x.AttemptConnectionToEndpoint();

                x.Listner.Tell(SelectionHandler.ChannelAcceptable.Instance);
                var channel = x.ExpectWorkerForCommand();

                EventFilter.Warning(pattern: new Regex("selector capacity limit")).Expect(1, () =>
                {
                    x.Listner.Tell(new TcpListener.FailedRegisterIncoming(channel));
                    AwaitCondition(() => !channel.IsOpen());
                });
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
            private readonly IPEndPoint _endpoint;
            
            private TestProbe _registerCallReceiver;
            private TestProbe _interestCallReceiver;
            
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
                _endpoint = TestUtils.TemporaryServerAddress();

                _registerCallReceiver = kit.CreateTestProbe();
                _interestCallReceiver = kit.CreateTestProbe();

                _parentRef = new TestActorRef<ListenerParent>(kit.Sys, Props.Create(() => new ListenerParent(this, pullMode)));
            }

            public void Run(Action<TestSetup> test)
            {
                _registerCallReceiver.ExpectMsg<SocketAsyncOperation>(x => (_pullMode && x == 0) || x == SocketAsyncOperation.Accept);
                test(this);
            }

            public void BindListener()
            {
                Listner.Tell(new ChannelRegistration(
                    x => _interestCallReceiver.Ref.Tell((int) x),
                    x => _interestCallReceiver.Ref.Tell(-(int) x)));
                _bindCommander.ExpectMsg<Tcp.Bound>();
            }

            public void AttemptConnectionToEndpoint()
            {
                new Socket(_endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp).Connect(_endpoint);
            }

            public IActorRef Listner { get { return _parentRef.UnderlyingActor.Listner; } }

            public TestProbe SelectorRouter
            {
                get { return _selectorRouter; }
            }

            public TestProbe InterestCallReceiver
            {
                get { return _interestCallReceiver; }
            }
            public TestProbe BindCommander { get { return _bindCommander; } }
            public TestProbe Parent { get { return _parent; } }

            public SocketChannel ExpectWorkerForCommand()
            {
                var message = _selectorRouter.ExpectMsg<SelectionHandler.WorkerForCommand>(TimeSpan.FromSeconds(10));
                var command = (TcpListener.RegisterIncoming) message.ApiCommand;
                command.Channel.IsOpen().ShouldBeTrue();
                message.Commander.ShouldBe(Listner);
                return command.Channel;
            }

            class ListenerParent : ActorBase, IChannelRegistry
            {
                private readonly TestSetup _test;
                private readonly bool _pullMode;
                private readonly IActorRef _listner;

                public ListenerParent(TestSetup test, bool pullMode)
                {
                    _test = test;
                    _pullMode = pullMode;

                    _listner = Context.ActorOf(Props.Create(() =>
                        new TcpListener(test._selectorRouter.Ref,
                            Tcp.Instance.Apply(Context.System),
                            this,
                            test._bindCommander.Ref,
                            new Tcp.Bind(_test._handler.Ref, test._endpoint, 100, new Inet.SocketOption[]{}, pullMode)))
                                                              .WithDeploy(Deploy.Local));

                    _test._parent.Watch(_listner);
                }

                internal IActorRef Listner { get { return _listner; } }

                protected override bool Receive(object message)
                {
                    _test._parent.Forward(message);
                    return true;
                }

                protected override SupervisorStrategy SupervisorStrategy()
                {
                    return Akka.Actor.SupervisorStrategy.StoppingStrategy;
                }

                public void Register(SocketChannel channel, SocketAsyncOperation? initialOps, IActorRef channelActor)
                {
                    _test._registerCallReceiver.Ref.Tell(initialOps, channelActor);
                }
            }
        }
    }
}
