//-----------------------------------------------------------------------
// <copyright file="RemoteDeploymentDeathWatchSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Xunit;

namespace Akka.Remote.Tests.MultiNode 
{
    public abstract class RemoteDeploymentDeathWatchSpec : MultiNodeSpec
    {
        private readonly RemoteDeploymentDeathWatchSpecConfig _specConfig;
        private readonly ITestKitAssertions _assertions;

        protected RemoteDeploymentDeathWatchSpec(Type type)
            : this(new RemoteDeploymentDeathWatchSpecConfig(), type)
        {
        }

        // A test class may only define a single public constructor.
        // https://github.com/xunit/xunit/blob/master/src/xunit.execution/Sdk/Frameworks/Runners/XunitTestClassRunner.cs#L178
        protected RemoteDeploymentDeathWatchSpec(RemoteDeploymentDeathWatchSpecConfig specConfig, Type type)
            : base(specConfig, type)
        {
            _specConfig = specConfig;
            _assertions = new XunitAssertions();
        }

        protected override int InitialParticipantsValueFactory
        {
            get { return Roles.Count; }
        }

        // Possible to override to let them heartbeat for a while.
        protected virtual void Sleep()
        {
        }


        protected virtual string Scenario
        {
            get { return string.Empty; }
        }

        [MultiNodeFact]
        public void An_actor_system_that_deploys_actors_on_another_node_must_be_able_to_shutdown_when_remote_node_crash()
        {
            RunOn(() =>
            {
                var hello = Sys.ActorOf(Props.Create(() => new Hello()), "hello");
                hello.Path.Address.ShouldBe(Node(_specConfig.Third).Address);
                EnterBarrier("hello-deployed");
                EnterBarrier("third-crashed");
                Sleep();

                // if the remote deployed actor is not removed the system will not shutdown
                var timeOut = RemainingOrDefault;
                try
                {
                    Sys.WhenTerminated.Wait(timeOut);
                }
                catch (TimeoutException ex)
                {
                    //TODO: add printTree

                    _assertions.Fail("Failed to stop {0} within {1} ", Sys.Name, timeOut);
                }
            }, _specConfig.Second);

            RunOn(() =>
            {
                EnterBarrier("hello-deployed");
                EnterBarrier("third-crashed");
            }, _specConfig.Third);

            RunOn(() =>
            {
                EnterBarrier("hello-deployed");
                Sleep();
                TestConductor.Exit(_specConfig.Third, 0).GetAwaiter().GetResult();
                EnterBarrier("third-crashed");

                //second system will be shutdown
                TestConductor.Shutdown(_specConfig.Second).GetAwaiter().GetResult();

                EnterBarrier("after-3");
            }, _specConfig.First);
        }

        internal class Hello : UntypedActor
        {
            protected override void OnReceive(object message)
            {
            }
        }
    }

    #region Several different variations of the test
    
    public class RemoteDeploymentNodeDeathWatchSlowSpec : RemoteDeploymentDeathWatchSpec
    {
        public RemoteDeploymentNodeDeathWatchSlowSpec():base(typeof(RemoteDeploymentNodeDeathWatchSlowSpec))
        { }

        protected override void Sleep()
        {
            Thread.Sleep(3000);
        }

        protected override string Scenario
        {
            get { return "slow"; }
        }
    }

    public class RemoteDeploymentNodeDeathWatchFastSpec : RemoteDeploymentDeathWatchSpec
    {
        public RemoteDeploymentNodeDeathWatchFastSpec() : base(typeof(RemoteDeploymentNodeDeathWatchFastSpec))
        { }
        
        protected override string Scenario
        {
            get { return "fast"; }
        }
    }
    
    #endregion

    public class RemoteDeploymentDeathWatchSpecConfig : MultiNodeConfig
    {
        public RemoteDeploymentDeathWatchSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = new Config(DebugConfig(false), ConfigurationFactory.ParseString(
                @"akka.loglevel = INFO
                  akka.remote.log-remote-lifecycle-events = off"
            ));

            DeployOn(Second, @"/hello.remote = ""@third@""");
        }

        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
    }
}
