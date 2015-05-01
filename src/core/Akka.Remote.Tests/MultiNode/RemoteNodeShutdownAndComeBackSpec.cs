using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.Util.Internal;

namespace Akka.Remote.Tests.MultiNode
{
    public class RemoteNodeShutdownAndComeBackSpecConfig : MultiNodeConfig
    {
        readonly RoleName _first;
        public RoleName First { get { return _first; } }
        readonly RoleName _second;
        public RoleName Second { get { return _second; } }

        public RemoteNodeShutdownAndComeBackSpecConfig(int port = 0)
        {
            _first = Role("first");
            _second = Role("second");
          
            CommonConfig =
                ConfigurationFactory.ParseString(String.Format(@"akka.remote.helios.tcp.port = {0}", port)).WithFallback(
                DebugConfig(true).WithFallback(
                ConfigurationFactory.ParseString(@"
                  akka.loglevel = DEBUG
                  akka.remote.log-remote-lifecycle-events = Debug
                  ## Keep it tight, otherwise reestablishing a connection takes too much time
                  akka.remote.transport-failure-detector.heartbeat-interval = 1 s
                  akka.remote.transport-failure-detector.acceptable-heartbeat-pause = 3 s
                  akka.remote.watch-failure-detector.acceptable-heartbeat-pause = 60 s
                  akka.remote.gate-invalid-addresses-for = 0.5 s
                   akka.loglevel = DEBUG
                    akka.remote {
                        log-received-messages = on
                        log-sent-messages = on
                    }
                    akka.actor.debug {
                        receive = on
                        fsm = on
                    }
                    akka.remote.log-remote-lifecycle-events = on
                    akka.log-dead-letters = on
                  akka.loggers = [""Akka.Logger.NLog.NLogLogger, Akka.Logger.NLog""]
            )")));

            TestTransport = true;
        }

        public class RemoteNodeShutdownAndComesBackMultiNode1 : RemoteNodeShutdownAndComeBackSpec
        {
            public RemoteNodeShutdownAndComesBackMultiNode1() : base(10000)
            { 
            }
        }

        public class RemoteNodeShutdownAndComesBackMultiNode2 : RemoteNodeShutdownAndComeBackSpec
        {
            public RemoteNodeShutdownAndComesBackMultiNode2() : base(10001)
            { 
            }
        }

        public class RemoteNodeShutdownAndComeBackSpec : MultiNodeSpec
        {
            readonly RemoteNodeShutdownAndComeBackSpecConfig _config;

            protected RemoteNodeShutdownAndComeBackSpec()
                : this(new RemoteNodeShutdownAndComeBackSpecConfig())
            {
            }

            protected RemoteNodeShutdownAndComeBackSpec(int port)
                : this(new RemoteNodeShutdownAndComeBackSpecConfig(port))
            {
            }

            protected override int InitialParticipantsValueFactory
            {
                get { return Roles.Count; }
            }

            private RemoteNodeShutdownAndComeBackSpec(RemoteNodeShutdownAndComeBackSpecConfig config)
                : base(config)
            {
                _config = config;
            }

            public IActorRef Identify(RoleName role, string actorName)
            {
                Sys.ActorSelection(Node(role) / "user" / actorName).Tell(new Identify(actorName));
                return ExpectMsg<ActorIdentity>().Subject;
            }

            [MultiNodeFact]
            public void
                RemoteNodeShutDownAndComesBackMustProperlyResetSystemMessageBufferStateWhenNewSystemWithSameAddressComesUp()
            {
                RunOn(() =>
                {
                    var secondAddress = Node(_config.Second).Address;
                    Sys.ActorOf(Props.Create(() => new Subject()), "subject1");
                    EnterBarrier("actors-started");

                    var subject = Identify(_config.Second, "subject");
                    var sysMsgBarrier = Identify(_config.Second, "sysmsgBarrier");

                    //Prime up the system message buffer
                    Watch(subject);
                    EnterBarrier("watch-established");

                    // Wait for proper system message propagation
                    // (Using a helper actor to ensure that all previous system messages arrived)
                    Watch(sysMsgBarrier);
                    Sys.Stop(sysMsgBarrier);
                    ExpectTerminated(sysMsgBarrier);

                    // Drop all messages from this point so no SHUTDOWN is ever received
                    TestConductor.Blackhole(_config.Second, _config.First, ThrottleTransportAdapter.Direction.Send)
                        .Wait();
                    // Shut down all existing connections so that the system can enter recovery mode (association attempts)
                    RARP.For(Sys)
                        .Provider.Transport.ManagementCommand(new ForceDisassociate(Node(_config.Second).Address))
                        .Wait(TimeSpan.FromSeconds(3));
                            
                    // Trigger reconnect attempt and also queue up a system message to be in limbo state (UID of remote system
                    // is unknown, and system message is pending)
                    Sys.Stop(subject);

                    Log.Info("Shutting down second");
                    // Get rid of old system -- now SHUTDOWN is lost
                    TestConductor.Shutdown(_config.Second).Wait();

                    // At this point the second node is restarting, while the first node is trying to reconnect without resetting
                    // the system message send state

                    // Now wait until second system becomes alive again
                    AwaitAssert(() =>
                    {
                        var p = CreateTestProbe();
                        Sys.ActorSelection(new RootActorPath(secondAddress) / "user" / "subject").Tell(new Identify("subject"), p.Ref);
                        p.ExpectMsg<ActorIdentity>(m => (string)m.MessageId == "subject" && m.Subject != null,
                            TimeSpan.FromSeconds(1));
                    }, TimeSpan.FromSeconds(30));

                    ExpectTerminated(subject);

                    // Establish watch with the new system. This triggers additional system message traffic. If buffers are out
                    // of synch the remote system will be quarantined and the rest of the test will fail (or even in earlier
                    // stages depending on circumstances).
                    Sys.ActorSelection(new RootActorPath(secondAddress)/"user"/"subject").Tell(new Identify("subject"));
                    var subjectNew = ExpectMsg<ActorIdentity>().Subject;
                    Watch(subjectNew);

                    subjectNew.Tell("shutdown");
                    FishForMessage(m =>
                    {
                        var terminated = m as Terminated;
                        if (terminated != null && terminated.ActorRef.Equals(subjectNew)) return true;
                        return false;
                    });
                }, _config.First);

                RunOn(() =>
                {
                    var addr = Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;
                    Sys.ActorOf(Props.Create(() => new Subject()), "subject");
                    Sys.ActorOf(Props.Create(() => new Subject()), "sysmsgBarrier");
                    var path = Node(_config.First);
                    EnterBarrier("actors-started");

                    EnterBarrier("watch-established");

                    Sys.AwaitTermination(TimeSpan.FromSeconds(30));

                    var config = String.Format(@"
                        akka.remote.helios.tcp {{
                          hostname = {0}
                          port = {1}
                        }}
                    ", addr.Host, addr.Port);

                    var freshSystem = ActorSystem.Create(Sys.Name, ConfigurationFactory.ParseString(config)
                        .WithFallback(Sys.Settings.Config));

                    var b = freshSystem.ActorOf(Props.Create(() => new Blah()));

                    freshSystem.EventStream.Subscribe(b, typeof (DeadLetter));

                    freshSystem.AwaitTermination(TimeSpan.FromSeconds(30));
                }, _config.Second);
            }

            class Blah : ReceiveActor
            {
                private ILoggingAdapter _log = Context.GetLogger();

                public Blah()
                {
                    Receive<DeadLetter>(l => _log.Warning("DeadLetter of {0}", l.Message.GetType()));
                }
            }

            class Subject : UntypedActor
            {
                protected override void OnReceive(object message)
                {
                    var @string = message as string;
                    if (@string == "shutdown")
                    {
                        Context.System.Shutdown();
                        return;
                    }

                    Sender.Tell(message);
                }
            }
        }
    }
}
