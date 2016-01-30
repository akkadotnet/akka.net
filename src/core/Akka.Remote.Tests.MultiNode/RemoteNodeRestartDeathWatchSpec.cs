//-----------------------------------------------------------------------
// <copyright file="RemoteWatcherSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Remote.Tests.MultiNode
{
    public abstract class RemoteNodeRestartDeathWatchSpec : MultiNodeSpec
    {
        private readonly RemoteNodeRestartDeathWatchSpecConfig _specConfig;

        protected RemoteNodeRestartDeathWatchSpec()
            : this(new RemoteNodeRestartDeathWatchSpecConfig())
        {
        }

        protected RemoteNodeRestartDeathWatchSpec(RemoteNodeRestartDeathWatchSpecConfig specConfig)
            : base(specConfig)
        {
            _specConfig = specConfig;
        }

        protected override int InitialParticipantsValueFactory
        {
            get { return Roles.Count; }
        }

        protected IActorRef Identify(RoleName role, string actorName)
        {
            Sys.ActorSelection(Node(role)/"user"/actorName).Tell(new Identify(actorName));
            return ExpectMsg<ActorIdentity>().Subject;
        }


        [MultiNodeFact]
        public void MustReceiveTerminatedWhenRemoteActorSystemIsRestarted()
        {
            
            RunOn(() =>
            {
                var secondAddress = Node(_specConfig.Second).Address;
                EnterBarrier("actors-started");

                var subject = Identify(_specConfig.Second, "subject");
                Watch(subject);
                subject.Tell("hello");
                ExpectMsg("hello");
                EnterBarrier("watch-established");
                
                // simulate a hard shutdown, nothing sent from the shutdown node
                TestConductor.Blackhole(_specConfig.Second, _specConfig.First, ThrottleTransportAdapter.Direction.Send)
                    .GetAwaiter()
                    .GetResult();
                TestConductor.Shutdown(_specConfig.Second).GetAwaiter().GetResult();
                ExpectTerminated(subject, TimeSpan.FromSeconds(20));
                Within(TimeSpan.FromSeconds(10), () =>
                {
                    // retry because the Subject actor might not be started yet
                    AwaitAssert(() =>
                    {
                        Sys.ActorSelection(new RootActorPath(secondAddress)/"user"/
                                           "subject").Tell("shutdown");
                        ExpectMsg<string>(msg => { return "shutdown-ack" == msg; }, TimeSpan.FromSeconds(1));
                    });
                });
            }, _specConfig.First);

            RunOn(() =>
            {
                var addr = Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;
                Sys.ActorOf(Props.Create(() => new Subject()), "subject");
                EnterBarrier("actors-started");

                EnterBarrier("watch-established");
                Sys.WhenTerminated.Wait(TimeSpan.FromSeconds(30));

                var sb = new StringBuilder().AppendLine("akka.remote.helios.tcp {").AppendLine("hostname = " + addr.Host)
                        .AppendLine("port = " + addr.Port)
                        .AppendLine("}");
                var freshSystem = ActorSystem.Create(Sys.Name,
                    ConfigurationFactory.ParseString(sb.ToString()).WithFallback(Sys.Settings.Config));
                freshSystem.ActorOf(Props.Create(() => new Subject()), "subject");

                freshSystem.WhenTerminated.Wait(TimeSpan.FromSeconds(30));
            }, _specConfig.Second);
        }

        private class Subject : ActorBase
        {
            protected override bool Receive(object message)
            {
                if ("shutdown".Equals(message))
                {
                    Sender.Tell("shutdown-ack");
                    Context.System.Terminate();
                }
                else
                {
                    Sender.Tell(message);
                }
                return true;
            }
        }
    }

    #region Several different variations of the test

    public class RemoteNodeRestartDeathWatchMultiNode1 : RemoteNodeRestartDeathWatchSpec
    {
    }

    public class RemoteNodeRestartDeathWatchMultiNode2 : RemoteNodeRestartDeathWatchSpec
    {
    }

    #endregion

    #region Config

    public class RemoteNodeRestartDeathWatchSpecConfig : MultiNodeConfig
    {
        public RemoteNodeRestartDeathWatchSpecConfig()
        {
            First = Role("first");
            Second = Role("second");

            CommonConfig = DebugConfig(false).WithFallback(ConfigurationFactory.ParseString(
                @"akka.loglevel = INFO
                  akka.remote.log-remote-lifecycle-events = off                    
                   akka.remote.transport-failure-detector.heartbeat-interval = 1 s
            akka.remote.transport-failure-detector.acceptable-heartbeat-pause = 3 s"
                ));
            TestTransport = true;
        }

        public RoleName First { get; }
        public RoleName Second { get; }
    }

    #endregion
}