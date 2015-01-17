using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests
{
    static class ChaosSupportExtensions
    {
        private static readonly Random random = new Random();

        internal static void Add(this GuaranteedDeliveryFailureSpec.IChaosSupport chaos, int i)
        {
            chaos.State.Add(i);
            if (chaos.State.Count >= GuaranteedDeliveryFailureSpec.NumberOfMessages)
            {
                chaos.Probe.Tell(new GuaranteedDeliveryFailureSpec.Done(chaos.State.ToArray()));
            }
        }

        internal static bool ShouldFail(double rate)
        {
            return random.NextDouble() < rate;
        }
    }

    public class GuaranteedDeliveryFailureSpec : AkkaSpec
    {
        #region internal test classes

        internal sealed class Start
        {
            public static readonly Start Instance = new Start();
            private Start() { }
        }

        internal struct Done
        {
            public Done(int[] vector)
                : this()
            {
                Vector = vector;
            }

            public int[] Vector { get; private set; }
        }

        internal struct ProcessingFailure
        {
            public ProcessingFailure(int x)
                : this()
            {
                I = x;
            }

            public int I { get; private set; }
        }
        internal struct JournalingFailure
        {
            public JournalingFailure(int x)
                : this()
            {
                I = x;
            }

            public int I { get; private set; }
        }

        internal struct Msg
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

        internal struct Confirm
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

        internal interface IEvt { }

        internal struct MsgSent : IEvt
        {
            public MsgSent(int x)
                : this()
            {
                I = x;
            }

            public int I { get; private set; }
        }

        internal struct MsgConfirmed : IEvt
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

        internal interface IChaosSupport
        {
            ActorRef Probe { get; }
            List<int> State { get; set; }
        }

        internal class ChaosSender : GuaranteedDeliveryActor
        {
            private readonly string _persistenceId;
            private readonly ActorRef _destination;
            private readonly Config _config;
            private readonly double _liveProcessingFailureRate;
            private readonly double _replayProcessingFailureRate;
            private LoggingAdapter _log;

            public LoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); }}

            public ChaosSender(ActorRef destination, ActorRef probe)
            {
                _destination = destination;
                Probe = probe;
                State = new List<int>();
                _persistenceId = "chaosSender";

                _config = Context.System.Settings.Config.GetConfig("akka.persistence.sender.chaos");
                _liveProcessingFailureRate = _config.GetDouble("live-processing-failure-rate");
                _replayProcessingFailureRate = _config.GetDouble("replay-processing-failure-rate");
            }

            public override string PersistenceId { get { return _persistenceId; } }
            protected override bool ReceiveRecover(object message)
            {
                return message.Match()
                    .With<IEvt>(UpdateState)
                    .With<RecoveryFailure>(() =>
                    {
                        // journal failed during recovery, throw exception to re-recover persistent actor
                        throw new TestException(DebugMessage("recovery failed"));
                    }).WasHandled;
            }

            protected override bool ReceiveCommand(object message)
            {
                return message.Match()
                    .With<int>(i =>
                    {
                        var failureRate = IsRecovering ? _replayProcessingFailureRate : _liveProcessingFailureRate;
                        if (State.Contains(i))
                            Log.Debug(DebugMessage("ignored duplicate"));
                        else
                            Persist(new MsgSent(i), sent =>
                            {
                                UpdateState(sent);
                                if (ChaosSupportExtensions.ShouldFail(failureRate))
                                    throw new TestException(DebugMessage("failed at payload " + sent.I));
                                else
                                    Log.Debug(DebugMessage("processed payload " + sent.I));
                            });
                    })
                    .With<Confirm>(confirm =>
                    {
                        Persist(new MsgConfirmed(confirm.DeliveryId, confirm.I), x => UpdateState(x));
                    })
                    .With<PersistenceFailure>(failure =>
                    {
                        failure.Payload.Match()
                            .With<MsgSent>(sent =>
                            {
                                // inform sender about journaling failure so that it can resend
                                Sender.Tell(new JournalingFailure(sent.I));
                            })
                            .With<MsgConfirmed>(() =>
                            {
                                // ok, will be redelivered
                            });
                    })
                    .WasHandled;
            }

            private void UpdateState(IEvt evt)
            {
                if (evt is MsgSent)
                {
                    var msg = (MsgSent)evt;
                    Add(msg.I);
                    Deliver(_destination.Path, deliveryId => new Msg(deliveryId, msg.I));
                }
                else if (evt is MsgConfirmed)
                {
                    var confirmation = (MsgConfirmed)evt;
                    ConfirmDelivery(confirmation.DeliveryId);
                }
            }

            private string DebugMessage(string msg)
            {
                return string.Format("[Sender] {0} (mode = {1}, seqNr = {2}, state = {3})",
                    msg, IsRecovering ? "replay" : "live", LastSequenceNr, string.Join(", ", State));
            }

            private void Add(int i)
            {
                State.Add(i);
                if (State.Count == NumberOfMessages) Probe.Tell(new Done(State.ToArray()));
            }


            public ActorRef Probe { get; private set; }
            public List<int> State { get; set; }
        }

        internal class ChaosDestination : ReceiveActor, IChaosSupport
        {
            private readonly Config _config;
            private readonly double _confirmFailureRate;
            private LoggingAdapter _log;

            public LoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }

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
                        Log.Error(string.Format("[destination] confirm message failed (message = {0}, {1})", m.DeliveryId, m.I));
                    }
                    else if (State.Contains(m.I))
                    {
                        Log.Debug(string.Format("[destination] ignored duplicate (message = {0}, {1})", m.DeliveryId, m.I));
                        Sender.Tell(new Confirm(m.DeliveryId, m.I));
                    }
                    else
                    {
                        this.Add(m.I);
                        Sender.Tell(new Confirm(m.DeliveryId, m.I));
                        Log.Debug(string.Format("[destination] received and confirmed (message = {0}, {1})", m.DeliveryId, m.I));
                    }
                });
            }

            public ActorRef Probe { get; private set; }
            public List<int> State { get; set; }
        }

        internal class ChaosApp : ReceiveActor
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

        #endregion

        private static readonly Config FailureSpecConfig = ConfigurationFactory.ParseString(
            @"akka.persistence.sender.chaos.live-processing-failure-rate = 0.3
              akka.persistence.sender.chaos.replay-processing-failure-rate = 0.1
              akka.persistence.destination.chaos.confirm-failure-rate = 0.3
              akka.persistence.journal.plugin = ""akka.persistence.journal.chaos""
              akka.persistence.journal.chaos.write-failure-rate = 0.3
              akka.persistence.journal.chaos.confirm-failure-rate = 0.2
              akka.persistence.journal.chaos.delete-failure-rate = 0.3
              akka.persistence.journal.chaos.replay-failure-rate = 0.25
              akka.persistence.journal.chaos.read-highest-failure-rate = 0.1
              akka.persistence.journal.chaos.class = Akka.Persistence.Tests.Journal.ChaosJournal
              akka.persistence.snapshot-store.local.dir = ""target/snapshots-at-least-once-delivery-failure-spec/""");

        internal const int NumberOfMessages = 10;

        public GuaranteedDeliveryFailureSpec()
            : base(FailureSpecConfig)
        {
        }

        [Fact]
        public void GuaranteedDelivery_must_tolerate_and_recover_from_random_failures()
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

}