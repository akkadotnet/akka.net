//-----------------------------------------------------------------------
// <copyright file="RemoteWatcherSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

        protected RemoteDeploymentDeathWatchSpec()
            : this(new RemoteDeploymentDeathWatchSpecConfig())
        {
        }

        protected RemoteDeploymentDeathWatchSpec(RemoteDeploymentDeathWatchSpecConfig specConfig)
            : base(specConfig)
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
        public void AnActorSystemThatDeploysActorsOnAnotherNodeMustBeAbleToShutdownWhenRemoteNodeCrash()
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
                    Sys.AwaitTermination(timeOut);
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
    public class RemoteDeploymentDeathWatchMultiNode1 : RemoteDeploymentDeathWatchSpec
    {
    }

    public class RemoteDeploymentDeathWatchMultiNode2 : RemoteDeploymentDeathWatchSpec
    {
    }

    public class RemoteDeploymentDeathWatchMultiNode3 : RemoteDeploymentDeathWatchSpec
    {
    }

    public class RemoteDeploymentNodeDeathWatchSlowSpec : RemoteDeploymentDeathWatchSpec
    {
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
        protected override string Scenario
        {
            get { return "fast"; }
        }
    }
    #endregion

    public class RemoteDeploymentDeathWatchSpecConfig : MultiNodeConfig
    {
        public RemoteDeploymentDeathWatchSpecConfig() : base()
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

        public RoleName First { get; private set; }
        public RoleName Second { get; private set; }
        public RoleName Third { get; private set; }
    }
}