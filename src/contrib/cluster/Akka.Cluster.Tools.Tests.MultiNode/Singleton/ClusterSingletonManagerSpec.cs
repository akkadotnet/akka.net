//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonManagerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Akka.TestKit.Internal.StringMatcher;
using Akka.TestKit.TestEvent;
using FluentAssertions;

namespace Akka.Cluster.Tools.Tests.MultiNode.Singleton
{
    public class ClusterSingletonManagerSpecConfig : MultiNodeConfig
    {
        public readonly RoleName Controller;
        public readonly RoleName Observer;
        public readonly RoleName First;
        public readonly RoleName Second;
        public readonly RoleName Third;
        public readonly RoleName Fourth;
        public readonly RoleName Fifth;
        public readonly RoleName Sixth;

        public ClusterSingletonManagerSpecConfig()
        {
            Controller = Role("controller");
            Observer = Role("observer");
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");
            Sixth = Role("sixth");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.auto-down-unreachable-after = 0s
            ")
            .WithFallback(ClusterSingletonManager.DefaultConfig())
            .WithFallback(ClusterSingletonProxy.DefaultConfig())
            .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            NodeConfig(new[] { First, Second, Third, Fourth, Fifth, Sixth }, new[] { ConfigurationFactory.ParseString(@"akka.cluster.roles = [worker]") });
        }
    }

    /**
     * This channel is extremely strict with regards to
     * registration and unregistration of consumer to
     * be able to detect misbehaviour (e.g. two active
     * singleton instances).
     */
    internal class PointToPointChannel : UntypedActor
    {
        #region messages

        public sealed class UnregisterConsumer
        {
            public static readonly UnregisterConsumer Instance = new UnregisterConsumer();

            private UnregisterConsumer()
            {
            }
        }

        public sealed class RegisterConsumer
        {
            public static readonly RegisterConsumer Instance = new RegisterConsumer();

            private RegisterConsumer()
            {
            }
        }

        public sealed class RegistrationOk
        {
            public static readonly RegistrationOk Instance = new RegistrationOk();

            private RegistrationOk()
            {
            }
        }

        public sealed class UnexpectedRegistration
        {
            public static readonly UnexpectedRegistration Instance = new UnexpectedRegistration();

            private UnexpectedRegistration()
            {
            }
        }

        public sealed class UnregistrationOk
        {
            public static readonly UnregistrationOk Instance = new UnregistrationOk();

            private UnregistrationOk()
            {
            }
        }

        public sealed class UnexpectedUnregistration
        {
            public static readonly UnexpectedUnregistration Instance = new UnexpectedUnregistration();

            private UnexpectedUnregistration()
            {
            }
        }

        public sealed class Reset
        {
            public static readonly Reset Instance = new Reset();

            private Reset()
            {
            }
        }

        public sealed class ResetOk
        {
            public static readonly ResetOk Instance = new ResetOk();

            private ResetOk()
            {
            }
        }

        #endregion

        private readonly ILoggingAdapter _log = Context.GetLogger();

        public PointToPointChannel()
        {
            Become(Idle);
        }

        private void Idle(object message)
        {
            message.Match()
                .With<RegisterConsumer>(_ =>
                {
                    _log.Info("Register consumer [{0}]", Sender.Path);
                    Sender.Tell(RegistrationOk.Instance);
                    Context.Become(Active(Sender));
                })
                .With<UnregisterConsumer>(_ =>
                {
                    _log.Info("Unexpected unregistration: [{0}]", Sender.Path);
                    Sender.Tell(UnexpectedRegistration.Instance);
                    Context.Stop(Self);
                })
                .With<Reset>(_ => Sender.Tell(ResetOk.Instance))
                .Default(msg => { });
        }

        private UntypedReceive Active(IActorRef consumer)
        {
            return message =>
            {
                message.Match()
                    .With<UnregisterConsumer>(_ =>
                    {
                        if (Sender.Equals(consumer))
                        {
                            _log.Info("UnregistrationOk: [{0}]", Sender.Path);
                            Sender.Tell(UnregistrationOk.Instance);
                            Context.Become(Idle);
                        }
                        else
                        {
                            _log.Info("UnexpectedUnregistration: [{0}], expected: [{1}]", Sender.Path, consumer.Path);
                            Sender.Tell(UnexpectedUnregistration.Instance);
                            Context.Stop(Self);
                        }
                    })
                    .With<RegisterConsumer>(_ =>
                    {
                        _log.Info("Unexpected RegisterConsumer: [{0}], active consumer: [{1}]", Sender.Path, consumer.Path);
                        Sender.Tell(UnexpectedRegistration.Instance);
                        Context.Stop(Self);
                    })
                    .With<Reset>(_ =>
                    {
                        Context.Become(Idle);
                        Sender.Tell(ResetOk.Instance);
                    })
                    .Default(msg => consumer.Tell(msg));
            };
        }

        protected override void OnReceive(object message) { }
    }

    internal class Consumer : ReceiveActor
    {
        private readonly IActorRef _queue;
        private readonly IActorRef _delegateTo;
        private readonly ILoggingAdapter _log = Context.GetLogger();

        #region messages

        public sealed class Ping
        {
            public static readonly Ping Instance = new Ping();

            private Ping()
            {
            }
        }

        public sealed class Pong
        {
            public static readonly Pong Instance = new Pong();

            private Pong()
            {
            }
        }

        public sealed class End
        {
            public static readonly End Instance = new End();

            private End()
            {
            }
        }

        public sealed class GetCurrent
        {
            public static readonly GetCurrent Instance = new GetCurrent();

            private GetCurrent()
            {
            }
        }

        #endregion

        private int _current = 0;
        private bool stoppedBeforeUnregistration = true;

        public Consumer(IActorRef queue, IActorRef delegateTo)
        {
            _queue = queue;
            _delegateTo = delegateTo;

            Receive<int>(n => n <= _current, n => Context.Stop(Self));
            Receive<int>(n =>
            {
                _current = n;
                _delegateTo.Tell(n);
            });
            Receive<PointToPointChannel.RegistrationOk>(x => _delegateTo.Tell(x));
            Receive<PointToPointChannel.UnexpectedRegistration>(x => _delegateTo.Tell(x));
            Receive<GetCurrent>(_ => Sender.Tell(_current));
            Receive<End>(_ => queue.Tell(PointToPointChannel.UnregisterConsumer.Instance));
            Receive<PointToPointChannel.UnregistrationOk>(_ =>
            {
                stoppedBeforeUnregistration = false;
                Context.Stop(Self);
            });
            Receive<Ping>(_ => Sender.Tell(Pong.Instance));
        }

        protected override void PreStart()
        {
            _queue.Tell(PointToPointChannel.RegisterConsumer.Instance);
        }

        protected override void PostStop()
        {
            if (stoppedBeforeUnregistration)
            {
                _log.Warning("Stopped before unregistration");
            }
        }
    }

    public class ClusterSingletonManagerSpec : MultiNodeClusterSpec
    {
        #region Setup

        private readonly TestProbe _identifyProbe;
        private readonly ActorPath _controllerRootActorPath;
        private int _msg = 0;

        private readonly RoleName _controller;
        private readonly RoleName _observer;
        private readonly RoleName _first;
        private readonly RoleName _second;
        private readonly RoleName _third;
        private readonly RoleName _fourth;
        private readonly RoleName _fifth;
        private readonly RoleName _sixth;

        public int Msg { get { return ++_msg; } }

        public IActorRef Queue
        {
            get
            {
                // this is used from inside actor construction, i.e. other thread, and must therefore not call `node(controller`
                Sys.ActorSelection(_controllerRootActorPath / "user" / "queue").Tell(new Identify("queue"), _identifyProbe.Ref);
                return _identifyProbe.ExpectMsg<ActorIdentity>().Subject;
            }
        }

        public ClusterSingletonManagerSpec() : this(new ClusterSingletonManagerSpecConfig())
        {
        }

        protected ClusterSingletonManagerSpec(ClusterSingletonManagerSpecConfig config) : base(config, typeof(ClusterSingletonManagerSpec))
        {
            _controller = config.Controller;
            _observer = config.Observer;
            _first = config.First;
            _second = config.Second;
            _third = config.Third;
            _fourth = config.Fourth;
            _fifth = config.Fifth;
            _sixth = config.Sixth;

            _identifyProbe = CreateTestProbe();
            _controllerRootActorPath = Node(config.Controller);
        }

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Join(Node(to).Address);
                if (Cluster.SelfRoles.Contains("worker"))
                {
                    CreateSingleton();
                    CreateSingletonProxy();
                }
            }, from);
        }

        private void AwaitMemberUp(TestProbe memberProbe, params RoleName[] nodes)
        {
            if (nodes.Length > 1)
            {
                RunOn(() =>
                {
                    memberProbe.ExpectMsg<ClusterEvent.MemberUp>(TimeSpan.FromSeconds(15)).Member.Address
                        .Should()
                        .Be(Node(nodes.First()).Address);
                }, nodes.Skip(1).ToArray());
            }

            RunOn(() =>
            {
                var roleNodes = nodes.Select(node => Node(node).Address);

                var addresses = memberProbe.ReceiveN(nodes.Length, TimeSpan.FromSeconds(15))
                    .Where(x => x is ClusterEvent.MemberUp)
                    .Select(x => (x as ClusterEvent.MemberUp).Member.Address);

                addresses.Except(roleNodes).Count().Should().Be(0);
            }, nodes.First());

            EnterBarrier(nodes[0].Name + "-up");
        }

        private void CreateSingleton()
        {
            Sys.ActorOf(ClusterSingletonManager.Props(
                singletonProps: Props.Create(() => new Consumer(Queue, TestActor)),
                terminationMessage: Consumer.End.Instance,
                settings: ClusterSingletonManagerSettings.Create(Sys).WithRole("worker")),
                name: "consumer");
        }

        private void CreateSingletonProxy()
        {
            Sys.ActorOf(ClusterSingletonProxy.Props(
                singletonManagerPath: "/user/consumer",
                settings: ClusterSingletonProxySettings.Create(Sys).WithRole("worker")),
                name: "consumerProxy");
        }

        private void VerifyProxyMsg(RoleName oldest, RoleName proxyNode, int msg)
        {
            EnterBarrier("before-" + msg + "-proxy-verified");

            // send message to the proxy
            RunOn(() =>
            {
                // make sure that the proxy has received membership changes
                // and points to the current singleton
                var p = CreateTestProbe();
                var oldestAddress = Node(oldest).Address;
                Within(TimeSpan.FromSeconds(10), () =>
                {
                    AwaitAssert(() =>
                    {
                        Sys.ActorSelection("/user/consumerProxy").Tell(Consumer.Ping.Instance, p.Ref);
                        p.ExpectMsg<Consumer.Pong>(TimeSpan.FromSeconds(1));
                        var replyFromAddress = p.LastSender.Path.Address;
                        if (oldest.Equals(proxyNode))
                            replyFromAddress.HasLocalScope.Should().BeTrue();
                        else
                            replyFromAddress.Should().Be(oldestAddress);
                    });
                });

                // send a real message
                Sys.ActorSelection("/user/consumerProxy").Tell(msg);
            }, proxyNode);

            EnterBarrier($"sent-msg-{msg}");

            // expect a message on the oldest node
            RunOn(() =>
            {
                ExpectMsg(msg);
            }, oldest);

            EnterBarrier("after-" + msg + "-proxy-verified");
        }

        private ActorSelection GetConsumer(RoleName oldest)
        {
            return Sys.ActorSelection(new RootActorPath(Node(oldest).Address) / "user" / "consumer" / "singleton");
        }

        private void VerifyRegistration(RoleName oldest)
        {
            EnterBarrier("before-" + oldest.Name + "-registration-verified");

            RunOn(() =>
            {
                ExpectMsg<PointToPointChannel.RegistrationOk>();
                GetConsumer(oldest).Tell(Consumer.GetCurrent.Instance);
                ExpectMsg(0);
            }, oldest);

            EnterBarrier("after-" + oldest.Name + "-registration-verified");
        }

        private void VerifyMsg(RoleName oldest, int msg)
        {
            EnterBarrier("before-" + msg + "-verified");

            RunOn(() =>
            {
                Queue.Tell(msg);
                // make sure it's not terminated, which would be wrong
                ExpectNoMsg(TimeSpan.FromSeconds(1));
            }, _controller);

            RunOn(() =>
            {
                ExpectMsg(msg, TimeSpan.FromSeconds(5));
            }, oldest);

            RunOn(() =>
            {
                ExpectNoMsg(TimeSpan.FromSeconds(1));
            }, Roles.Where(r => r != oldest && r != _controller && r != _observer).ToArray());

            EnterBarrier("after-" + msg + "-verified");
        }

        private void Crash(params RoleName[] roles)
        {
            RunOn(() =>
            {
                Queue.Tell(PointToPointChannel.Reset.Instance);
                ExpectMsg<PointToPointChannel.ResetOk>();
                foreach (var role in roles)
                {
                    Log.Info("Shutdown [{0}]", GetAddress(role));
                    TestConductor.Exit(role, 0).Wait();
                }
            }, _controller);
        }

        #endregion


        [MultiNodeFact]
        public void ClusterSingletonManagerSpecs()
        {
            ClusterSingletonManager_should_startup_6_node_cluster();
            ClusterSingletonManager_should_let_the_proxy_messages_to_the_singleton_in_a_6_node_cluster();
            ClusterSingletonManager_should_handover_when_oldest_leaves_in_6_node_cluster();
            ClusterSingletonManager_should_takeover_when_oldest_crashes_in_5_node_cluster();
            ClusterSingletonManager_should_takeover_when_two_oldest_crash_in_3_node_cluster();
            ClusterSingletonManager_should_takeover_when_oldest_crashes_in_2_node_cluster();
        }

        public void ClusterSingletonManager_should_startup_6_node_cluster()
        {
            Within(TimeSpan.FromSeconds(60), () =>
            {
                var memberProbe = CreateTestProbe();
                Cluster.Subscribe(memberProbe.Ref, new[] { typeof(ClusterEvent.MemberUp) });
                memberProbe.ExpectMsg<ClusterEvent.CurrentClusterState>();

                RunOn(() =>
                {
                    // watch that it is not terminated, which would indicate misbehaviour
                    Watch(Sys.ActorOf(Props.Create<PointToPointChannel>(), "queue"));
                }, _controller);
                EnterBarrier("queue-started");

                Join(_first, _first);
                AwaitMemberUp(memberProbe, _first);
                VerifyRegistration(_first);
                VerifyMsg(_first, Msg);

                // join the observer node as well, which should not influence since it doesn't have the "worker" role
                Join(_observer, _first);
                AwaitMemberUp(memberProbe, _observer, _first);
                VerifyProxyMsg(_first, _first, Msg);

                Join(_second, _first);
                AwaitMemberUp(memberProbe, _second, _observer, _first);
                VerifyMsg(_first, Msg);
                VerifyProxyMsg(_first, _second, Msg);

                Join(_third, _first);
                AwaitMemberUp(memberProbe, _third, _second, _observer, _first);
                VerifyMsg(_first, Msg);
                VerifyProxyMsg(_first, _third, Msg);

                Join(_fourth, _first);
                AwaitMemberUp(memberProbe, _fourth, _third, _second, _observer, _first);
                VerifyMsg(_first, Msg);
                VerifyProxyMsg(_first, _fourth, Msg);

                Join(_fifth, _first);
                AwaitMemberUp(memberProbe, _fifth, _fourth, _third, _second, _observer, _first);
                VerifyMsg(_first, Msg);
                VerifyProxyMsg(_first, _fifth, Msg);

                Join(_sixth, _first);
                AwaitMemberUp(memberProbe, _sixth, _fifth, _fourth, _third, _second, _observer, _first);
                VerifyMsg(_first, Msg);
                VerifyProxyMsg(_first, _sixth, Msg);

                EnterBarrier("after-1");
            });
        }

        public void ClusterSingletonManager_should_let_the_proxy_messages_to_the_singleton_in_a_6_node_cluster()
        {
            Within(TimeSpan.FromSeconds(60), () =>
            {
                VerifyProxyMsg(_first, _first, Msg);
                VerifyProxyMsg(_first, _second, Msg);
                VerifyProxyMsg(_first, _third, Msg);
                VerifyProxyMsg(_first, _fourth, Msg);
                VerifyProxyMsg(_first, _fifth, Msg);
                VerifyProxyMsg(_first, _sixth, Msg);
            });
        }

        public void ClusterSingletonManager_should_handover_when_oldest_leaves_in_6_node_cluster()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                var leaveNode = _first;

                RunOn(() =>
                {
                    Cluster.Leave(GetAddress(leaveNode));
                }, leaveNode);

                VerifyRegistration(_second);
                VerifyMsg(_second, Msg);
                VerifyProxyMsg(_second, _second, Msg);
                VerifyProxyMsg(_second, _third, Msg);
                VerifyProxyMsg(_second, _fourth, Msg);
                VerifyProxyMsg(_second, _fifth, Msg);
                VerifyProxyMsg(_second, _sixth, Msg);

                RunOn(() =>
                {
                    Sys.ActorSelection("/user/consumer").Tell(new Identify("singleton"), _identifyProbe.Ref);
                    _identifyProbe.ExpectMsg<ActorIdentity>(i =>
                    {
                        if (i.MessageId.Equals("singleton") && i.Subject != null)
                        {
                            Watch(i.Subject);
                            ExpectTerminated(i.Subject);
                        }
                    });
                }, leaveNode);
                EnterBarrier("after-leave");
            });
        }

        public void ClusterSingletonManager_should_takeover_when_oldest_crashes_in_5_node_cluster()
        {
            Within(TimeSpan.FromSeconds(60), () =>
            {
                // mute logging of deadLetters during shutdown of systems
                if (!Log.IsDebugEnabled)
                    Sys.EventStream.Publish(new Mute(new DeadLettersFilter(new PredicateMatcher(s => true), new PredicateMatcher(s => true))));
                EnterBarrier("logs-muted");

                Crash(_second);
                VerifyRegistration(_third);
                VerifyMsg(_third, Msg);
                VerifyProxyMsg(_third, _third, Msg);
                VerifyProxyMsg(_third, _fourth, Msg);
                VerifyProxyMsg(_third, _fifth, Msg);
                VerifyProxyMsg(_third, _sixth, Msg);
            });
        }

        public void ClusterSingletonManager_should_takeover_when_two_oldest_crash_in_3_node_cluster()
        {
            Within(TimeSpan.FromSeconds(60), () =>
            {
                Crash(_third, _fourth);
                VerifyRegistration(_fifth);
                VerifyMsg(_fifth, Msg);
                VerifyProxyMsg(_fifth, _fifth, Msg);
                VerifyProxyMsg(_fifth, _sixth, Msg);
            });
        }

        public void ClusterSingletonManager_should_takeover_when_oldest_crashes_in_2_node_cluster()
        {
            Within(TimeSpan.FromSeconds(60), () =>
            {
                Crash(_fifth);
                VerifyRegistration(_sixth);
                VerifyMsg(_sixth, Msg);
                VerifyProxyMsg(_sixth, _sixth, Msg);
            });
        }
    }
}
