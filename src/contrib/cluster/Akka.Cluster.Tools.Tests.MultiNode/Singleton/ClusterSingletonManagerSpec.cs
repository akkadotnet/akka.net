//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonManagerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Akka.TestKit.Internal.StringMatcher;
using Akka.TestKit.TestEvent;
using FluentAssertions;

namespace Akka.Cluster.Tools.Tests.MultiNode.Singleton;

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
            .WithFallback(ClusterSingleton.DefaultConfig())
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
        public static readonly UnregisterConsumer Instance = new();

        private UnregisterConsumer()
        {
        }
    }

    public sealed class RegisterConsumer
    {
        public static readonly RegisterConsumer Instance = new();

        private RegisterConsumer()
        {
        }
    }

    public sealed class RegistrationOk
    {
        public static readonly RegistrationOk Instance = new();

        private RegistrationOk()
        {
        }
    }

    public sealed class UnexpectedRegistration
    {
        public static readonly UnexpectedRegistration Instance = new();

        private UnexpectedRegistration()
        {
        }
    }

    public sealed class UnregistrationOk
    {
        public static readonly UnregistrationOk Instance = new();

        private UnregistrationOk()
        {
        }
    }

    public sealed class UnexpectedUnregistration
    {
        public static readonly UnexpectedUnregistration Instance = new();

        private UnexpectedUnregistration()
        {
        }
    }

    public sealed class Reset
    {
        public static readonly Reset Instance = new();

        private Reset()
        {
        }
    }

    public sealed class ResetOk
    {
        public static readonly ResetOk Instance = new();

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
        switch (message)
        {
            case RegisterConsumer _:
                _log.Info("Register consumer [{0}]", Sender.Path);
                Sender.Tell(RegistrationOk.Instance);
                Context.Become(Active(Sender));
                break;
            case UnregisterConsumer _:
                _log.Info("Unexpected unregistration: [{0}]", Sender.Path);
                Sender.Tell(UnexpectedRegistration.Instance);
                Context.Stop(Self);
                break;
            case Reset _:
                Sender.Tell(ResetOk.Instance);
                break;
            default:
                // no-op
                break;
        }
    }

    private UntypedReceive Active(IActorRef consumer)
    {
        return message =>
        {
            switch (message)
            {
                case UnregisterConsumer _:
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
                    break;
                    
                case RegisterConsumer _:
                    _log.Info("Unexpected RegisterConsumer: [{0}], active consumer: [{1}]", Sender.Path, consumer.Path);
                    Sender.Tell(UnexpectedRegistration.Instance);
                    Context.Stop(Self);
                    break;
                    
                case Reset _:
                    Context.Become(Idle);
                    Sender.Tell(ResetOk.Instance);
                    break;
                    
                default:
                    consumer.Tell(message);
                    break;
            }
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
        public static readonly Ping Instance = new();

        private Ping()
        {
        }
    }

    public sealed class Pong
    {
        public static readonly Pong Instance = new();

        private Pong()
        {
        }
    }

    public sealed class End
    {
        public static readonly End Instance = new();

        private End()
        {
        }
    }

    public sealed class GetCurrent
    {
        public static readonly GetCurrent Instance = new();

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

        Receive<int>(n => n <= _current, _ => Context.Stop(Self));
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

    private async Task Join(RoleName from, RoleName to)
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
        await EnterBarrierAsync(from.Name + "-joined");
    }

    private async Task AwaitMemberUp(TestProbe memberProbe, params RoleName[] nodes)
    {
        if (nodes.Length > 1)
        {
            await RunOnAsync(async () =>
            {
                (await memberProbe.ExpectMsgAsync<ClusterEvent.MemberUp>(TimeSpan.FromSeconds(15))).Member.Address
                    .Should()
                    .Be((await NodeAsync(nodes.First())).Address);
            }, nodes.Skip(1).ToArray());
        }

        RunOn(() =>
        {
            var roleNodes = nodes.Select(node => Node(node).Address);

            var addresses = memberProbe.ReceiveN(nodes.Length, TimeSpan.FromSeconds(15))
                .Where(x => x is ClusterEvent.MemberUp)
                .Select(x => ((ClusterEvent.MemberUp)x).Member.Address);

            addresses.Except(roleNodes).Count().Should().Be(0);
        }, nodes.First());

        await EnterBarrierAsync(nodes[0].Name + "-up");
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

    private async Task VerifyProxyMsg(RoleName oldest, RoleName proxyNode, int msg)
    {
        await EnterBarrierAsync("before-" + msg + "-proxy-verified");

        // send message to the proxy
        await RunOnAsync(async () =>
        {
            // make sure that the proxy has received membership changes
            // and points to the current singleton
            var p = CreateTestProbe();
            var oldestAddress = (await NodeAsync(oldest)).Address;
            await WithinAsync(TimeSpan.FromSeconds(10), async () =>
            {
                await AwaitAssertAsync(async () =>
                {
                    Sys.ActorSelection("/user/consumerProxy").Tell(Consumer.Ping.Instance, p.Ref);
                    await p.ExpectMsgAsync<Consumer.Pong>(TimeSpan.FromSeconds(1));
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

        await EnterBarrierAsync($"sent-msg-{msg}");

        // expect a message on the oldest node
        await RunOnAsync(async () =>
        {
            await ExpectMsgAsync(msg);
        }, oldest);

        await EnterBarrierAsync("after-" + msg + "-proxy-verified");
    }

    private ActorSelection GetConsumer(RoleName oldest)
    {
        return Sys.ActorSelection(new RootActorPath(Node(oldest).Address) / "user" / "consumer" / "singleton");
    }

    private async Task VerifyRegistration(RoleName oldest)
    {
        await EnterBarrierAsync("before-" + oldest.Name + "-registration-verified");

        await RunOnAsync(async () =>
        {
            await ExpectMsgAsync<PointToPointChannel.RegistrationOk>();
            GetConsumer(oldest).Tell(Consumer.GetCurrent.Instance);
            ExpectMsg(0);
        }, oldest);

        await EnterBarrierAsync("after-" + oldest.Name + "-registration-verified");
    }

    private async Task VerifyMsg(RoleName oldest, int msg)
    {
        await EnterBarrierAsync("before-" + msg + "-verified");

        await RunOnAsync(async () =>
        {
            Queue.Tell(msg);
            // make sure it's not terminated, which would be wrong
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
        }, _controller);

        await RunOnAsync(async () =>
        {
            await ExpectMsgAsync(msg, TimeSpan.FromSeconds(5));
        }, oldest);

        await RunOnAsync(async () =>
        {
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
        }, Roles.Where(r => r != oldest && r != _controller && r != _observer).ToArray());

        await EnterBarrierAsync("after-" + msg + "-verified");
    }

    private async Task Crash(params RoleName[] roles)
    {
        await RunOnAsync(async () =>
        {
            Queue.Tell(PointToPointChannel.Reset.Instance);
            await ExpectMsgAsync<PointToPointChannel.ResetOk>();
            foreach (var role in roles)
            {
                Log.Info("Shutdown [{0}]", GetAddress(role));
                await TestConductor.ExitAsync(role, 0);
            }
        }, _controller);
    }

    #endregion


    [MultiNodeFact]
    public async Task ClusterSingletonManagerSpecs()
    {
        await ClusterSingletonManager_should_startup_6_node_cluster();
        await ClusterSingletonManager_should_let_the_proxy_messages_to_the_singleton_in_a_6_node_cluster();
        await ClusterSingletonManager_should_handover_when_oldest_leaves_in_6_node_cluster();
        await ClusterSingletonManager_should_takeover_when_oldest_crashes_in_5_node_cluster();
        await ClusterSingletonManager_should_takeover_when_two_oldest_crash_in_3_node_cluster();
        await ClusterSingletonManager_should_takeover_when_oldest_crashes_in_2_node_cluster();
    }

    public async Task ClusterSingletonManager_should_startup_6_node_cluster()
    {
        await WithinAsync(TimeSpan.FromSeconds(60), async () =>
        {
            var memberProbe = CreateTestProbe();
            Cluster.Subscribe(memberProbe.Ref, new[] { typeof(ClusterEvent.MemberUp) });
            await memberProbe.ExpectMsgAsync<ClusterEvent.CurrentClusterState>();

            await RunOnAsync(async () =>
            {
                // watch that it is not terminated, which would indicate misbehaviour
                await WatchAsync(Sys.ActorOf(Props.Create<PointToPointChannel>(), "queue"));
            }, _controller);
            await EnterBarrierAsync("queue-started");

            await Join(_first, _first);
            await AwaitMemberUp(memberProbe, _first);
            await VerifyRegistration(_first);
            await VerifyMsg(_first, Msg);

            // join the observer node as well, which should not influence since it doesn't have the "worker" role
            await Join(_observer, _first);
            await AwaitMemberUp(memberProbe, _observer, _first);
            await VerifyProxyMsg(_first, _first, Msg);

            await Join(_second, _first);
            await AwaitMemberUp(memberProbe, _second, _observer, _first);
            await VerifyMsg(_first, Msg);
            await VerifyProxyMsg(_first, _second, Msg);

            await Join(_third, _first);
            await AwaitMemberUp(memberProbe, _third, _second, _observer, _first);
            await VerifyMsg(_first, Msg);
            await VerifyProxyMsg(_first, _third, Msg);

            await Join(_fourth, _first);
            await AwaitMemberUp(memberProbe, _fourth, _third, _second, _observer, _first);
            await VerifyMsg(_first, Msg);
            await VerifyProxyMsg(_first, _fourth, Msg);

            await Join(_fifth, _first);
            await AwaitMemberUp(memberProbe, _fifth, _fourth, _third, _second, _observer, _first);
            await VerifyMsg(_first, Msg);
            await VerifyProxyMsg(_first, _fifth, Msg);

            await Join(_sixth, _first);
            await AwaitMemberUp(memberProbe, _sixth, _fifth, _fourth, _third, _second, _observer, _first);
            await VerifyMsg(_first, Msg);
            await VerifyProxyMsg(_first, _sixth, Msg);

            await EnterBarrierAsync("after-1");
        });
    }

    public async Task ClusterSingletonManager_should_let_the_proxy_messages_to_the_singleton_in_a_6_node_cluster()
    {
        await WithinAsync(TimeSpan.FromSeconds(60), async () =>
        {
            await VerifyProxyMsg(_first, _first, Msg);
            await VerifyProxyMsg(_first, _second, Msg);
            await VerifyProxyMsg(_first, _third, Msg);
            await VerifyProxyMsg(_first, _fourth, Msg);
            await VerifyProxyMsg(_first, _fifth, Msg);
            await VerifyProxyMsg(_first, _sixth, Msg);
        });
    }

    public async Task ClusterSingletonManager_should_handover_when_oldest_leaves_in_6_node_cluster()
    {
        await WithinAsync(TimeSpan.FromSeconds(30), async () =>
        {
            var leaveNode = _first;

            RunOn(() =>
            {
                Cluster.Leave(GetAddress(leaveNode));
            }, leaveNode);

            await VerifyRegistration(_second);
            await VerifyMsg(_second, Msg);
            await VerifyProxyMsg(_second, _second, Msg);
            await VerifyProxyMsg(_second, _third, Msg);
            await VerifyProxyMsg(_second, _fourth, Msg);
            await VerifyProxyMsg(_second, _fifth, Msg);
            await VerifyProxyMsg(_second, _sixth, Msg);

            await RunOnAsync(async () =>
            {
                Sys.ActorSelection("/user/consumer").Tell(new Identify("singleton"), _identifyProbe.Ref);
                await _identifyProbe.ExpectMsgAsync<ActorIdentity>(i =>
                {
                    if (i.MessageId.Equals("singleton") && i.Subject != null)
                    {
                        Watch(i.Subject);
                        ExpectTerminated(i.Subject);
                    }
                });
            }, leaveNode);
            await EnterBarrierAsync("after-leave");
        });
    }

    public async Task ClusterSingletonManager_should_takeover_when_oldest_crashes_in_5_node_cluster()
    {
        await WithinAsync(TimeSpan.FromSeconds(60), async () =>
        {
            // mute logging of deadLetters during shutdown of systems
            if (!Log.IsDebugEnabled)
                Sys.EventStream.Publish(new Mute(new DeadLettersFilter(new PredicateMatcher(_ => true), new PredicateMatcher(_ => true))));
            await EnterBarrierAsync("logs-muted");

            await Crash(_second);
            await VerifyRegistration(_third);
            await VerifyMsg(_third, Msg);
            await VerifyProxyMsg(_third, _third, Msg);
            await VerifyProxyMsg(_third, _fourth, Msg);
            await VerifyProxyMsg(_third, _fifth, Msg);
            await VerifyProxyMsg(_third, _sixth, Msg);
        });
    }

    public async Task ClusterSingletonManager_should_takeover_when_two_oldest_crash_in_3_node_cluster()
    {
        await WithinAsync(TimeSpan.FromSeconds(60), async () =>
        {
            await Crash(_third, _fourth);
            await VerifyRegistration(_fifth);
            await VerifyMsg(_fifth, Msg);
            await VerifyProxyMsg(_fifth, _fifth, Msg);
            await VerifyProxyMsg(_fifth, _sixth, Msg);
        });
    }

    public async Task ClusterSingletonManager_should_takeover_when_oldest_crashes_in_2_node_cluster()
    {
        await WithinAsync(TimeSpan.FromSeconds(60), async () =>
        {
            await Crash(_fifth);
            await VerifyRegistration(_sixth);
            await VerifyMsg(_sixth, Msg);
            await VerifyProxyMsg(_sixth, _sixth, Msg);
        });
    }
}