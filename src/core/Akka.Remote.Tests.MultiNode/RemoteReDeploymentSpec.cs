//-----------------------------------------------------------------------
// <copyright file="RemoteReDeploymentSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using FluentAssertions;

namespace Akka.Remote.Tests.MultiNode
{
    public class RemoteReDeploymentSpecConfig : MultiNodeConfig
    {
        public RemoteReDeploymentSpecConfig()
        {
            First = Role("first");
            Second = Role("second");

            CommonConfig = DebugConfig(false).WithFallback(ConfigurationFactory.ParseString(@"
    akka.remote.transport-failure-detector {
         threshold=0.1
         heartbeat-interval=0.1s
         acceptable-heartbeat-pause=1s
       }
       akka.remote.watch-failure-detector {
         threshold=0.1
         heartbeat-interval=0.1s
         acceptable-heartbeat-pause=2.5s
       }"));

            DeployOn(Second, "/parent/hello.remote = \"@first@\"");

            TestTransport = true;
        }

        public RoleName First { get; }
        public RoleName Second { get; }
    }


    public abstract class RemoteReDeploymentSpec : MultiNodeSpec
    {
        private readonly RemoteReDeploymentSpecConfig _config;

        protected RemoteReDeploymentSpec(Type type) : this(new RemoteReDeploymentSpecConfig(), type)
        {
        }

        protected RemoteReDeploymentSpec(RemoteReDeploymentSpecConfig config, Type type) : base(config, type)
        {
            _config = config;
        }

        protected abstract bool ExpectQuarantine { get; }
        protected abstract TimeSpan SleepAfterKill { get; }

        protected override int InitialParticipantsValueFactory
        {
            get { return Roles.Count; }
        }

        [MultiNodeFact]
        public async Task RemoteReDeployment_must_terminate_the_child_when_its_parent_system_is_replaced_by_a_new_one()
        {
            var echo = Sys.ActorOf(EchoProps(TestActor), "echo");
            await EnterBarrierAsync("echo-started");

            await RunOnAsync(async () =>
            {
                Sys.ActorOf(Props.Create(() => new Parent()), "parent")
                    .Tell(new ParentMessage(Props.Create(() => new Hello()), "hello"));

                await ExpectMsgAsync("HelloParent", TimeSpan.FromSeconds(15));
            }, _config.Second);

            await RunOnAsync(async () =>
            {
                await ExpectMsgAsync("PreStart", TimeSpan.FromSeconds(15));
            }, _config.First);

            await EnterBarrierAsync("first-deployed");

            await RunOnAsync(async () =>
            {
                // Read the address while `second` is still registered with the conductor. The
                // replacement system has to come back on this exact address, otherwise the remote
                // deployment `first` holds points at nothing and every later step fails for a
                // reason that has nothing to do with re-deployment.
                var addressBeforeRestart = (await NodeAsync(_config.Second)).Address;

                await TestConductor.BlackholeAsync(_config.Second, _config.First, ThrottleTransportAdapter.Direction.Both);
                await TestConductor.ShutdownAsync(_config.Second, true);
                if (ExpectQuarantine)
                {
                    
                    await WithinAsync(SleepAfterKill, async () => 
                    {
                        await ExpectMsgAsync("PostStop");
                        //need to pad the timing here, since `ExpectNoMsg` will wait until exactly SleepAfterKill and fail the spec
                        await ExpectNoMsgAsync(Remaining - TimeSpan.FromSeconds(0.2));
                    });
                }
                else
                {
                    await ExpectNoMsgAsync(SleepAfterKill);
                }
                // The conductor parks an address query for a role it has no registration for and
                // answers it the moment that role registers again, so awaiting this once is the
                // handshake that the replacement node is back. Polling it instead queues further
                // queries behind the first one, and the conductor client drops a query that
                // arrives while another is still outstanding.
                var addressAfterRestart = (await NodeAsync(_config.Second)).Address;
                addressAfterRestart.Should().Be(addressBeforeRestart,
                    "the replacement system on `second` must take over the address of the system it replaces");
            }, _config.First);

            ActorSystem tempSys = null;

            await RunOnAsync(async () =>
            {
                var addressBeforeRestart = ((ExtendedActorSystem)Sys).Provider.DefaultAddress;

                await Sys.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(30));
                await ExpectNoMsgAsync(SleepAfterKill);
                tempSys = await StartNewSystemAsync();

                ((ExtendedActorSystem)tempSys).Provider.DefaultAddress.Should().Be(addressBeforeRestart,
                    "the replacement system must bind the address of the system it replaces");
            }, _config.Second);

            await EnterBarrierAsync("cable-cut");

            await RunOnAsync(async () =>
            {
                await TestConductor.PassThroughAsync(_config.Second, _config.First, ThrottleTransportAdapter.Direction.Both);
            }, _config.First);

            await RunOnAsync(async () =>
            {
                var firstEcho = await NodeAsync(_config.First) / "user" / "echo";
                var sel = tempSys.ActorSelection(firstEcho);
                var probe = CreateTestProbe(tempSys);

                // Bound the retry loop here rather than nesting it inside a `within` block. A
                // nested block that outruns its own budget loses the failure it was carrying, and
                // the spec then walks into `ready-again` on an association that never came back:
                // `first` reports a barrier timeout and `second` reports a 60 second barrier ask
                // timeout, neither of which names the real problem. The 15 second budget also
                // nests inside the 30 second barrier budget `first` spends waiting at
                // `ready-again`.
                await probe.AwaitAssertAsync(async () =>
                {
                    sel.Tell(new Identify("id-echo-again"), probe.Ref);
                    var identity = await probe.ExpectMsgAsync<ActorIdentity>(TimeSpan.FromSeconds(2));
                    if (identity.Subject is null)
                        throw new InvalidOperationException("echo on `first` not reachable yet");
                }, TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(500));
            }, _config.Second);

            await EnterBarrierAsync("ready-again");

            await RunOnAsync(async () =>
            {
                var p = CreateTestProbe(tempSys);
                tempSys.ActorOf(EchoProps(p.Ref), "echo");
                p.Send(tempSys.ActorOf(Props.Create(() => new Parent()), "parent"),
                    new ParentMessage(Props.Create(() => new Hello()), "hello"));
                await p.ExpectMsgAsync("HelloParent", TimeSpan.FromSeconds(15));
            }, _config.Second);

            await EnterBarrierAsync("re-deployed");

            await RunOnAsync(async () =>
            {
                await WithinAsync(TimeSpan.FromSeconds(15), async () =>
                {
                    if (ExpectQuarantine)
                    {
                        await ExpectMsgAsync("PreStart");
                    }
                    else
                    {
                        await foreach (var _ in ExpectMsgAllOfAsync(new []{ "PostStop", "PreStart" })) { }
                    }
                });
            }, _config.First);

            await EnterBarrierAsync("the-end");

            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));

            await EnterBarrierAsync("stopping");

            await RunOnAsync(async () =>
            {
                await tempSys.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
            }, _config.Second);
        }

        private Props EchoProps(IActorRef target)
        {
            return Props.Create(() => new Echo(target));
        }

        private sealed class Parent : ActorBase
        {
            private readonly ActorSelection _monitor;

            public Parent()
            {
                _monitor = Context.ActorSelection("/user/echo");
            }

            protected override bool Receive(object message)
            {
                if (message is ParentMessage msg)
                {
                    Context.ActorOf(msg.Props, msg.Name); 
                    return true;
                }

                _monitor.Tell(message);
                return true;
            }
        }

        private sealed class Echo : ActorBase
        {
            private readonly IActorRef _target;

            public Echo(IActorRef target)
            {
                _target = target;
            }

            protected override bool Receive(object message)
            {
                //Context.GetLogger().Info("received {0} from {1}", message, Sender);
                _target.Tell(message);
                return true;
            }
        }

        private sealed class Hello : ActorBase
        {
            private readonly ActorSelection _monitor;

            public Hello()
            {
                Context.Parent.Tell("HelloParent");
                _monitor = Context.ActorSelection("/user/echo");
            }

            protected override bool Receive(object message)
            {
                return true;
            }

            protected override void PreStart()
            {
                _monitor.Tell("PreStart");
            }

            protected override void PostStop()
            {
                _monitor.Tell("PostStop");
            }
        }

        private sealed class ParentMessage
        {
            public ParentMessage(Props props, string name)
            {
                Props = props;
                Name = name;
            }

            public Props Props { get; }
            public string Name { get; }
        }
    }

    #region specs

    public class RemoteReDeploymentFastMultiNetSpec : RemoteReDeploymentSpec
    {
        public RemoteReDeploymentFastMultiNetSpec() : base(typeof(RemoteReDeploymentFastMultiNetSpec))
        { }

        // new association will come in while old is still "healthy"
        protected override bool ExpectQuarantine
        {
            get { return false; }
        }

        protected override TimeSpan SleepAfterKill
        {
            get { return TimeSpan.FromSeconds(0); }
        }
    }

    public class RemoteReDeploymentMediumMultiNetSpec : RemoteReDeploymentSpec
    {
        public RemoteReDeploymentMediumMultiNetSpec():base(typeof(RemoteReDeploymentMediumMultiNetSpec))
        { }

        // new association will come in while old is gated in ReliableDeliverySupervisor
        protected override bool ExpectQuarantine
        {
            get { return false; }
        }

        protected override TimeSpan SleepAfterKill
        {
            get { return TimeSpan.FromSeconds(1); }
        }
    }

    public class RemoteReDeploymentSlowMultiNetSpec : RemoteReDeploymentSpec
    {
        public RemoteReDeploymentSlowMultiNetSpec():base(typeof(RemoteReDeploymentSlowMultiNetSpec))
        { }

        // new association will come in after old has been quarantined
        protected override bool ExpectQuarantine
        {
            get { return true; }
        }

        protected override TimeSpan SleepAfterKill
        {
            get { return TimeSpan.FromSeconds(10); }
        }

    }

    #endregion
}
