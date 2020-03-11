//-----------------------------------------------------------------------
// <copyright file="RemoteNodeShutdownAndComesBackSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;

namespace Akka.Remote.Tests.MultiNode
{
    public class RemoteNodeShutdownAndComesBackMultiNetSpec : MultiNodeConfig
    {
        public RemoteNodeShutdownAndComesBackMultiNetSpec()
        {
            First = Role("first");
            Second = Role("second");

            CommonConfig = DebugConfig(false).WithFallback(ConfigurationFactory.ParseString(@"
                  akka.loglevel = INFO
                  akka.remote.log-remote-lifecycle-events = INFO
                  ## Keep it tight, otherwise reestablishing a connection takes too much time
                  akka.remote.transport-failure-detector.heartbeat-interval = 1 s
                  akka.remote.transport-failure-detector.acceptable-heartbeat-pause = 3 s
                  ## the acceptable pause is too long and therefore the test will fail, it pass when we use a lower value like the default one
                  ## akka.remote.watch-failure-detector.acceptable-heartbeat-pause = 60 s
            "));

            TestTransport = true;
        }

        public RoleName First { get; }
        public RoleName Second { get; }

        public sealed class Subject : ReceiveActor
        {
            public Subject()
            {
                Receive<string>(_ => Context.System.Terminate(), s => "shutdown".Equals(s));
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }
    }

    public class RemoteNodeShutdownAndComesBackSpec : MultiNodeSpec
    {
        private readonly RemoteNodeShutdownAndComesBackMultiNetSpec _config;
        private readonly Func<RoleName, string, IActorRef> _identify;

        public RemoteNodeShutdownAndComesBackSpec() : this(new RemoteNodeShutdownAndComesBackMultiNetSpec())
        {
        }

        protected RemoteNodeShutdownAndComesBackSpec(RemoteNodeShutdownAndComesBackMultiNetSpec config) : base(config, typeof(RemoteNodeShutdownAndComesBackSpec))
        {
            _config = config;

            _identify = (role, actorName) =>
            {
                Sys.ActorSelection(Node(role) / "user" / actorName).Tell(new Identify(actorName));
                return ExpectMsg<ActorIdentity>().Subject;
            };
        }

        protected override int InitialParticipantsValueFactory { get; } = 2;

        [MultiNodeFact]
        public void RemoteNodeShutdownAndComesBack_must_properly_reset_system_message_buffer_state_when_new_system_with_same_Address_comes_up()
        {
            RunOn(() =>
            {
                var secondAddress = Node(_config.Second).Address;
                Sys.ActorOf<RemoteNodeShutdownAndComesBackMultiNetSpec.Subject>("subject");
                EnterBarrier("actors-started");

                var subject = _identify(_config.Second, "subject");
                var sysmsgBarrier = _identify(_config.Second, "sysmsgBarrier");

                // Prime up the system message buffer
                Watch(subject);
                EnterBarrier("watch-established");

                // Wait for proper system message propagation
                // (Using a helper actor to ensure that all previous system messages arrived)
                Watch(sysmsgBarrier);
                Sys.Stop(sysmsgBarrier);
                ExpectTerminated(sysmsgBarrier);

                // Drop all messages from this point so no SHUTDOWN is ever received
                TestConductor.Blackhole(_config.Second, _config.First, ThrottleTransportAdapter.Direction.Send).Wait();

                // Shut down all existing connections so that the system can enter recovery mode (association attempts)
                RARP.For(Sys)
                    .Provider.Transport.ManagementCommand(new ForceDisassociate(Node(_config.Second).Address))
                    .Wait(TimeSpan.FromSeconds(3));

                // Trigger reconnect attempt and also queue up a system message to be in limbo state (UID of remote system
                // is unknown, and system message is pending)
                Sys.Stop(subject);

                // Get rid of old system -- now SHUTDOWN is lost
                TestConductor.Shutdown(_config.Second).Wait();

                // At this point the second node is restarting, while the first node is trying to reconnect without resetting
                // the system message send state

                // Now wait until second system becomes alive again
                Within(TimeSpan.FromSeconds(30), () =>
                {
                    // retry because the Subject actor might not be started yet
                    AwaitAssert(() =>
                    {
                        var p = CreateTestProbe();
                        Sys.ActorSelection(new RootActorPath(secondAddress) / "user" / "subject").Tell(new Identify("subject"), p.Ref);
                        p.ExpectMsg<ActorIdentity>(i => i.Subject != null && "subject".Equals(i.MessageId), TimeSpan.FromSeconds(1));
                    });
                });

                ExpectTerminated(subject, TimeSpan.FromSeconds(10));

                // Establish watch with the new system. This triggers additional system message traffic. If buffers are out
                // of sync the remote system will be quarantined and the rest of the test will fail (or even in earlier
                // stages depending on circumstances).
                Sys.ActorSelection(new RootActorPath(secondAddress) / "user" / "subject").Tell(new Identify("subject"));
                var subjectNew = ExpectMsg<ActorIdentity>(i => i.Subject != null).Subject;
                Watch(subjectNew);

                subjectNew.Tell("shutdown");
                // we are waiting for a Terminated here, but it is ok if it does not arrive
                ReceiveWhile(TimeSpan.FromSeconds(5), msg => msg as ActorIdentity);
            }, _config.First);

            RunOn(() =>
            {
                var addr = ((ExtendedActorSystem)Sys).Provider.DefaultAddress;
                Sys.ActorOf<RemoteNodeShutdownAndComesBackMultiNetSpec.Subject>("subject");
                Sys.ActorOf<RemoteNodeShutdownAndComesBackMultiNetSpec.Subject>("sysmsgBarrier");
                EnterBarrier("actors-started");

                EnterBarrier("watch-established");

                Sys.WhenTerminated.Wait(TimeSpan.FromSeconds(30));

                var freshConfig = new StringBuilder().AppendLine("akka.remote.dot-netty.tcp {").AppendLine("hostname = " + addr.Host)
                        .AppendLine("port = " + addr.Port)
                        .AppendLine("}").ToString();

                var freshSystem = ActorSystem.Create(Sys.Name,
                    ConfigurationFactory.ParseString(freshConfig)
                    .WithFallback(Sys.Settings.Config));

                freshSystem.ActorOf<RemoteNodeShutdownAndComesBackMultiNetSpec.Subject>("subject");

                freshSystem.WhenTerminated.Wait(TimeSpan.FromSeconds(30));
            }, _config.Second);
        }
    }
}
