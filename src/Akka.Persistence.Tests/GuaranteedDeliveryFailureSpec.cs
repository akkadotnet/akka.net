using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests
{
    public class GuaranteedDeliveryFailureSpec : AkkaSpec
    {
        private Config _config;
        internal const int NumberOfMessages = 10;

        public GuaranteedDeliveryFailureSpec()
        {
            _config = ConfigurationFactory.ParseString(
            @"akka.persistence.sender.chaos.live-processing-failure-rate = 0.3
              akka.persistence.sender.chaos.replay-processing-failure-rate = 0.1
              akka.persistence.destination.chaos.confirm-failure-rate = 0.3
              akka.persistence.journal.plugin = ""akka.persistence.journal.chaos""
              akka.persistence.journal.chaos.write-failure-rate = 0.3
              akka.persistence.journal.chaos.confirm-failure-rate = 0.2
              akka.persistence.journal.chaos.delete-failure-rate = 0.3
              akka.persistence.journal.chaos.replay-failure-rate = 0.25
              akka.persistence.journal.chaos.read-highest-failure-rate = 0.1
              akka.persistence.journal.chaos.class = akka.persistence.journal.chaos.ChaosJournal
              akka.persistence.snapshot-store.local.dir = ""target/snapshots-at-least-once-delivery-failure-spec/""");
        }

        [Fact]
        public void AtLeastOnceDelivery_must_tolerate_and_recover_from_random_failures()
        {
            var chaos = Sys.ActorOf(Props.Create(() => new ChaosApp(TestActor)), "chaosApp");
            chaos.Tell(Start.Instance);
            ExpectDone();   // by sender
            ExpectDone();   // by destination

            // recovery of the new instance should have same outcome
            var chaos2 = Sys.ActorOf(Props.Create(() => new ChaosApp(TestActor)), "chaosApp2");
            ExpectDone();   // by sender

            // destination won't receive message again, beacuse all of the has already been confirmed
        }

        private void ExpectDone()
        {
            Within(TimeSpan.FromSeconds(NumberOfMessages), () => 
                ExpectMsg<Done>().Vector.OrderBy(x => x)
                .ShouldOnlyContainInOrder(Enumerable.Range(1, NumberOfMessages).ToArray()));
        }
    }

    struct Start
    {
        public static readonly Start Instance = new Start();
    }

    struct Done
    {
        public Done(int[] vector)
            : this()
        {
            Vector = vector;
        }

        public int[] Vector { get; private set; }
    }

    struct ProcessingFailure
    {
        public ProcessingFailure(int x)
            : this()
        {
            I = x;
        }

        public int I { get; private set; }
    }
    struct JournalingFailure
    {
        public JournalingFailure(int x)
            : this()
        {
            I = x;
        }

        public int I { get; private set; }
    }

    struct Msg
    {
        public Msg(long deliveryId, int x)
            : this()
        {
            I = x;
            DeliveryId = deliveryId;
        }

        public int I { get; private set; }
        public long DeliveryId { get; private set; }
    }
    struct Confirm
    {
        public Confirm(long deliveryId, int x)
            : this()
        {
            I = x;
            DeliveryId = deliveryId;
        }

        public int I { get; private set; }
        public long DeliveryId { get; private set; }
    }

    interface IEvt
    {
    }

    struct MsgSent : IEvt
    {
        public MsgSent(int x)
            : this()
        {
            I = x;
        }

        public int I { get; private set; }
    }
    struct MsgConfirmed : IEvt
    {
        public MsgConfirmed(long deliveryId, int x)
            : this()
        {
            DeliveryId = deliveryId;
            I = x;
        }

        public long DeliveryId { get; private set; }
        public int I { get; private set; }
    }

    interface IChaosSupport
    {
        ActorRef Probe { get; }
        List<int> State { get; set; }
    }

    static class ChaosSupportExtensions
    {
        private static readonly Random random = new Random();

        internal static void Add(this IChaosSupport chaos, int i)
        {
            chaos.State.Add(i);
            if (chaos.State.Count >= GuaranteedDeliveryFailureSpec.NumberOfMessages)
            {
                chaos.Probe.Tell(new Done(chaos.State.ToArray()));
            }
        }

        internal static bool ShouldFail(double rate)
        {
            return random.NextDouble() < rate;
        }
    }

    class ChaosSender : PersistentActorBase
    {
        private readonly ActorRef _destination;
        private readonly Config _config;
        private readonly double _liveProcessingFailureRate;
        private readonly double _replayProcessingFailureRate;

        public ChaosSender(ActorRef destination, ActorRef probe)
        {
            _destination = destination;
            Probe = probe;
            State = new List<int>();
            PersistenceId = "chaosSender";

            _config = Context.System.Settings.Config.GetConfig("akka.persistence.sender.chaos");
            _liveProcessingFailureRate = _config.GetDouble("live-processing-failure-rate");
            _replayProcessingFailureRate = _config.GetDouble("replay-processing-failure-rate");
        }

        public string PersistenceId { get; set; }

        public ActorRef Probe { get; private set; }
        public List<int> State { get; set; }
    }

    class ChaosDestination : ReceiveActor, IChaosSupport
    {
        private readonly Config _config;
        private readonly double _confirmFailureRate;

        public ChaosDestination(ActorRef probe)
        {
            Probe = probe;
            State = new List<int>();
            _config = Context.System.Settings.Config.GetConfig("akka.persistence.destination.chaos");
            _confirmFailureRate = _config.GetDouble("confirm-failure-rate");

            Receive<Msg>(m =>
            {
                if (ChaosSupportExtensions.ShouldFail(_confirmFailureRate))
                {

                }
                else if (State.Contains(m.I))
                {
                    Sender.Tell(new Confirm(m.DeliveryId, m.I));
                }
                else
                {
                    this.Add(m.I);
                    Sender.Tell(new Confirm(m.DeliveryId, m.I));
                }
            });
        }

        public ActorRef Probe { get; private set; }
        public List<int> State { get; set; }
    }

    class ChaosApp : ReceiveActor
    {
        private readonly ActorRef _probe;
        private readonly ActorRef _destination;
        private readonly ActorRef _sender;

        public ChaosApp(ActorRef probe)
        {
            _probe = probe;
            _destination = Context.ActorOf(Props.Create(() => new ChaosDestination(_probe)), "destination");
            _sender = Context.ActorOf(Props.Create(() => new ChaosSender(_destination, _probe)), "sender");

            Receive<Start>(_ =>
            {
                for (int i = 1; i < GuaranteedDeliveryFailureSpec.NumberOfMessages; i++)
                {
                    _sender.Tell(i);
                }
            });
            Receive<ProcessingFailure>(x => _sender.Tell(x.I));
            Receive<JournalingFailure>(x => _sender.Tell(x.I));
        }
    }
}