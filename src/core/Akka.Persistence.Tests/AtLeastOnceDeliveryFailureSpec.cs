//-----------------------------------------------------------------------
// <copyright file="AtLeastOnceDeliveryFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Tests
{
    static class ChaosSupportExtensions
    {
        private static readonly Random random = new Random();

        internal static void Add(this AtLeastOnceDeliveryFailureSpec.IChaosSupport chaos, int i)
        {
            chaos.State.Add(i);
            if (chaos.State.Count == AtLeastOnceDeliveryFailureSpec.NumberOfMessages)
            {
                chaos.Probe.Tell(new AtLeastOnceDeliveryFailureSpec.Done(chaos.State.ToArray()));
            }
        }

        internal static bool ShouldFail(double rate)
        {
            return random.NextDouble() < rate;
        }
    }

    public class AtLeastOnceDeliveryFailureSpec : AkkaSpec
    {
        #region internal test classes

        internal sealed class Start
        {
            public static readonly Start Instance = new Start();
            private Start() { }
        }

        internal struct Done
        {
            public Done(int[] ints)
                : this()
            {
                Ints = ints;
            }

            public int[] Ints { get; private set; }
        }

        internal struct Ack
        {
            public Ack(int i)
                : this()
            {
                I = i;
            }

            public int I { get; private set; }
        }

        internal struct Msg
        {
            public Msg(long deliveryId, int i)
                : this()
            {
                I = i;
                DeliveryId = deliveryId;
            }

            public int I { get; private set; }
            public long DeliveryId { get; private set; }
        }

        internal struct Confirm
        {
            public Confirm(long deliveryId, int i)
                : this()
            {
                I = i;
                DeliveryId = deliveryId;
            }

            public int I { get; private set; }
            public long DeliveryId { get; private set; }
        }

        internal interface IEvt { }

        internal struct MsgSent : IEvt
        {
            public MsgSent(int i)
                : this()
            {
                I = i;
            }

            public int I { get; private set; }
        }

        internal struct MsgConfirmed : IEvt
        {
            public MsgConfirmed(long deliveryId, int i)
                : this()
            {
                DeliveryId = deliveryId;
                I = i;
            }

            public long DeliveryId { get; private set; }
            public int I { get; private set; }
        }

        internal interface IChaosSupport
        {
            IActorRef Probe { get; }
            List<int> State { get; set; }
        }

        internal class ChaosSender : AtLeastOnceDeliveryActor
        {
            private readonly IActorRef _destination;
            private readonly Config _config;
            private readonly double _liveProcessingFailureRate;
            private readonly double _replayProcessingFailureRate;
            private ILoggingAdapter _log;

            public ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); }}

            public ChaosSender(IActorRef destination, IActorRef probe) 
                : base(x => x.WithRedeliverInterval(TimeSpan.FromMilliseconds(500)))
            {
                _destination = destination;
                Probe = probe;
                State = new List<int>();

                _config = Context.System.Settings.Config.GetConfig("akka.persistence.sender.chaos");
                if (_config.IsNullOrEmpty())
                    throw ConfigurationException.NullOrEmptyConfig<ChaosSender>("akka.persistence.sender.chaos");

                _liveProcessingFailureRate = _config.GetDouble("live-processing-failure-rate", 0);
                _replayProcessingFailureRate = _config.GetDouble("replay-processing-failure-rate", 0);
            }

            public override string PersistenceId { get { return "chaosSender"; } }

            protected override bool ReceiveRecover(object message)
            {
                return message.Match()
                    .With<IEvt>(evt =>
                    {
                        UpdateState(evt);
                        if (ChaosSupportExtensions.ShouldFail(_replayProcessingFailureRate))
                            throw new TestException(DebugMessage(string.Format("replay failed at event {0}", evt)));
                        Log.Debug(DebugMessage(string.Format("replayed event {0}", evt)));
                    }).WasHandled;
            }

            protected override bool ReceiveCommand(object message)
            {
                return message.Match()
                    .With<int>(i =>
                    {
                        if (State.Contains(i))
                        {
                            Log.Debug(DebugMessage("ignored duplicate"));
                            Sender.Tell(new Ack(i));
                        }
                        else
                            Persist(new MsgSent(i), sent =>
                            {
                                UpdateState(sent);
                                if (ChaosSupportExtensions.ShouldFail(_liveProcessingFailureRate))
                                    throw new TestException(DebugMessage(string.Format("failed at payload {0}", sent.I)));
                                Log.Debug(DebugMessage(String.Format("processed payload {0}", sent.I)));
                            });
                    })
                    .With<Confirm>(confirm =>
                    {
                        Persist(new MsgConfirmed(confirm.DeliveryId, confirm.I), x => UpdateState(x));
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

            protected override void OnRecoveryFailure(Exception reason, object message = null)
            {
                // mute logging
            }

            protected override void OnPersistFailure(Exception cause, object @event, long sequenceNr)
            {
                // mute logging
            }

            public IActorRef Probe { get; private set; }
            public List<int> State { get; set; }
        }

        internal class ChaosDestination : ReceiveActor, IChaosSupport
        {
            private readonly Config _config;
            private readonly double _confirmFailureRate;
            private ILoggingAdapter _log;

            public ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }

            public ChaosDestination(IActorRef probe)
            {
                Probe = probe;
                State = new List<int>();
                _config = Context.System.Settings.Config.GetConfig("akka.persistence.destination.chaos");
                _confirmFailureRate = _config.GetDouble("confirm-failure-rate", 0);

                Receive<Msg>(m =>
                {
                    if (ChaosSupportExtensions.ShouldFail(_confirmFailureRate))
                    {
                        Log.Debug("[destination] confirm message failed (message = {0}, {1})", m.DeliveryId, m.I);
                    }
                    else if (State.Contains(m.I))
                    {
                        Log.Debug("[destination] ignored duplicate (message = {0}, {1})", m.DeliveryId, m.I);
                        Sender.Tell(new Confirm(m.DeliveryId, m.I));
                    }
                    else
                    {
                        this.Add(m.I);
                        Sender.Tell(new Confirm(m.DeliveryId, m.I));
                        Log.Debug("[destination] received and confirmed (message = {0}, {1})", m.DeliveryId, m.I);
                    }
                });
            }

            public IActorRef Probe { get; private set; }
            public List<int> State { get; set; }
        }

        internal class ChaosApp : ReceiveActor
        {
            private readonly IActorRef _probe;
            private readonly IActorRef _destination;
            private IActorRef _sender;
            private readonly ISet<int> _acks;
            private readonly ILoggingAdapter _log;

            public ChaosApp(IActorRef probe)
            {
                _probe = probe;
                _destination = Context.ActorOf(Props.Create(() => new ChaosDestination(_probe)), "destination");
                _sender = CreateSender();
                _acks = new HashSet<int>();
                _log = Context.GetLogger();

                Receive<Start>(_ =>
                {
                    for (int i = 1; i <= NumberOfMessages; i++)
                    {
                        _sender.Tell(i);
                    }
                });
                Receive<Ack>(x => _acks.Add(x.I));
                Receive<Terminated>(x =>
                {
                    // snd will be stopped if recover or persist fail
                    _log.Debug("sender stopped, starting it again");
                    _sender = CreateSender();
                    for (var i = 1; i <= NumberOfMessages; i++)
                    {
                        if (!_acks.Contains(i))
                            _sender.Tell(i);
                    }
                });
            }

            private IActorRef CreateSender()
            {
                var sender =  Context.ActorOf(Props.Create(() => new ChaosSender(_destination, _probe)), "sender");
                Context.Watch(sender);
                return sender;
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
              akka.persistence.journal.chaos.class = ""Akka.Persistence.Tests.Journal.ChaosJournal, Akka.Persistence.Tests""
              akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.local""
              akka.persistence.snapshot-store.local.dir = ""target/snapshots-at-least-once-delivery-failure-spec/""");

        internal const int NumberOfMessages = 10;

        public AtLeastOnceDeliveryFailureSpec(ITestOutputHelper output)
            : base(FailureSpecConfig.WithFallback(Persistence.DefaultConfig()), output)
        {
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

            // destination doesn't receive messages again, because all have been confirmed already
        }

        private void ExpectDone()
        {
            Within(TimeSpan.FromSeconds(NumberOfMessages), () =>
            {
                var vec = ExpectMsg<Done>().Ints;
                vec.OrderBy(x => x).ShouldOnlyContainInOrder(Enumerable.Range(1, NumberOfMessages).ToArray());
            });
        }
    }

}
