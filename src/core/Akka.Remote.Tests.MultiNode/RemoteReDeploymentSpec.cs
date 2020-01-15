//-----------------------------------------------------------------------
// <copyright file="RemoteReDeploymentSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;

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
        private readonly Func<RoleName, string, IActorRef> _identify;

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
        public void RemoteReDeployment_must_terminate_the_child_when_its_parent_system_is_replaced_by_a_new_one()
        {
            var echo = Sys.ActorOf(EchoProps(TestActor), "echo");
            EnterBarrier("echo-started");

            RunOn(() =>
            {
                Sys.ActorOf(Props.Create(() => new Parent()), "parent")
                    .Tell(new ParentMessage(Props.Create(() => new Hello()), "hello"));

                ExpectMsg<string>("HelloParent", TimeSpan.FromSeconds(15));
            }, _config.Second);

            RunOn(() =>
            {
                ExpectMsg<string>("PreStart", TimeSpan.FromSeconds(15));
                
            }, _config.First);

            EnterBarrier("first-deployed");

            RunOn(() =>
            {
                TestConductor.Blackhole(_config.Second, _config.First, ThrottleTransportAdapter.Direction.Both)
                    .Wait();
                TestConductor.Shutdown(_config.Second, true).Wait();
                if (ExpectQuarantine)
                {
                    
                    Within(SleepAfterKill, () => 
                    {
                        ExpectMsg("PostStop");
                        //need to pad the timing here, since `ExpectNoMsg` will wait until exactly SleepAfterKill and fail the spec
                        ExpectNoMsg(Remaining - TimeSpan.FromSeconds(0.2));
                    });
                }
                else
                {
                    ExpectNoMsg(SleepAfterKill);
                }
                AwaitAssert(() => Node(_config.Second), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100));
            }, _config.First);

            ActorSystem tempSys = null;

            RunOn(() =>
            {
                Sys.WhenTerminated.Wait(TimeSpan.FromSeconds(30));
                ExpectNoMsg(SleepAfterKill);
                tempSys = StartNewSystem();
            }, _config.Second);

            EnterBarrier("cable-cut");

            RunOn(() =>
            {
                var p = CreateTestProbe(tempSys);
                tempSys.ActorOf(EchoProps(p.Ref), "echo");
                p.Send(tempSys.ActorOf(Props.Create(() => new Parent()), "parent"),
                    new ParentMessage(Props.Create(() => new Hello()), "hello"));
                p.ExpectMsg("HelloParent", TimeSpan.FromSeconds(15));
            }, _config.Second);

            EnterBarrier("re-deployed");

            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(15), () =>
                {
                    if (ExpectQuarantine)
                    {
                        ExpectMsg("PreStart");
                    }
                    else
                    {
                        ExpectMsgAllOf("PostStop", "PreStart");
                    }
                });
            }, _config.First);

            EnterBarrier("the-end");

            ExpectNoMsg(TimeSpan.FromSeconds(1));
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
                return message.Match().With<ParentMessage>(_ => Context.ActorOf(_.Props, _.Name)).Default(m =>
                    _monitor.Tell(m)).WasHandled;
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
