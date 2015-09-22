namespace Akka.Remote.Tests.MultiNode
{
    using System;
    using System.Threading;

    using Akka.Actor;
    using Akka.Configuration;
    using Akka.Remote.TestKit;

    public class RemoteNodeDeathWatchMultiNetSpec : MultiNodeConfig
    {
        public RoleName First { get; set; }

        public RoleName Second { get; set; }

        public RoleName Third { get; set; }

        public RemoteNodeDeathWatchMultiNetSpec()
        {
            CommonConfig = DebugConfig(false)
                                    .WithFallback(ConfigurationFactory.ParseString(@"
      akka.loglevel = INFO
      akka.remote.log-remote-lifecycle-events = off
      ## Use a tighter setting than the default, otherwise it takes 20s for DeathWatch to trigger
      akka.remote.watch-failure-detector.acceptable-heartbeat-pause = 3 s
"));
        }

        public sealed class WatchIt
        {
            public IActorRef Watchee { get; private set; }

            public WatchIt(IActorRef watchee)
            {
                Watchee = watchee;
            }
        }

        public sealed class UnwatchIt
        {
            public IActorRef Watchee { get; private set; }

            public UnwatchIt(IActorRef watchee)
            {
                Watchee = watchee;
            }
        }

        public class Ack { }

        public class WrappedTerminated
        {
            public Terminated Message { get; private set; }

            public WrappedTerminated(Terminated message)
            {
                Message = message;
            }
        }

        public class ProbeActor : UntypedActor
        {
            private readonly IActorRef testActor;

            public ProbeActor(IActorRef testActor)
            {
                this.testActor = testActor;
            }

            protected override void OnReceive(object message)
            {
                if (message is WatchIt)
                {
                    Context.Watch(((WatchIt)message).Watchee);
                    Sender.Tell(new Ack());
                }
                else if(message is UnwatchIt)
                {
                    Context.Unwatch(((UnwatchIt)message).Watchee);
                    Sender.Tell(new Ack());
                }
                else if (message is Terminated)
                {
                    testActor.Forward(new WrappedTerminated((Terminated)message));
                }
                else
                {
                    testActor.Forward(message);
                }
            }
        }
    }

    public class RemoteNodeDeathWatchSpec : MultiNodeSpec
    {
        private readonly RemoteNodeDeathWatchMultiNetSpec _config;

        protected string Scenario { get; set; }

        protected Action Sleep { get; set; }

        public RemoteNodeDeathWatchSpec()
            :this(new RemoteNodeDeathWatchMultiNetSpec())
        {
            
        }

        public RemoteNodeDeathWatchSpec(RemoteNodeDeathWatchMultiNetSpec config)
            :base (config)
        {
            _config = config;

            this.MuteDeadLetters(messageClasses: typeof(RemoteWatcher.Heartbeat));
        }

        protected override int InitialParticipantsValueFactory
        {
            get
            {
                return Roles.Count;
            }
        }
    }
}