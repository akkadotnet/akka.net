//-----------------------------------------------------------------------
// <copyright file="AtLeastOnceDeliveryReceiveActorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests
{
    [Collection(nameof(AtLeastOnceDeliveryReceiveActorSpec))]
    public class AtLeastOnceDeliveryReceiveActorSpec : PersistenceSpec
    {
        #region internal test classes

        [Serializable]
        private sealed class AcceptedReq : IEvt
        {
            public AcceptedReq(string payload, string destinationPath)
            {
                Payload = payload;
                DestinationPath = destinationPath;
            }

            public string Payload { get; private set; }

            //FIXME: change to Akka.Actor.ActorPath when serialization problems will be solved
            public string DestinationPath { get; private set; }
        }

        [Serializable]
        private sealed class Action : IEquatable<Action>
        {
            public Action(long id, string payload)
            {
                Id = id;
                Payload = payload;
            }

            public long Id { get; private set; }
            public string Payload { get; private set; }

            public bool Equals(Action other)
            {
                return Id == other.Id && string.Equals(Payload, other.Payload);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                return obj is Action && Equals((Action) obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Id.GetHashCode()*397) ^ (Payload != null ? Payload.GetHashCode() : 0);
                }
            }
        }

        [Serializable]
        private sealed class ActionAck : IEquatable<ActionAck>
        {
            public ActionAck(long id)
            {
                Id = id;
            }

            public long Id { get; private set; }

            public bool Equals(ActionAck other)
            {
                return other != null && other.Id == Id;
            }
        }

        [Serializable]
        private sealed class Boom
        {
            public static readonly Boom Instance = new Boom();
        }

        private class Destination : ReceiveActor
        {
            private readonly ISet<long> _allReceived;

            public Destination(IActorRef testActor)
            {
                _allReceived = new HashSet<long>();
                Receive<Action>(a =>
                {
                    if (!_allReceived.Contains(a.Id))
                    {
                        testActor.Tell(a);

                        _allReceived.Add(a.Id);
                    }
                    Sender.Tell(new ActionAck(a.Id));
                });
            }
        }

        private interface IEvt
        {
        }

        [Serializable]
        private sealed class InvalidReq
        {
            public static readonly InvalidReq Instance = new InvalidReq();

            private InvalidReq()
            {
            }

            public override bool Equals(object obj)
            {
                return obj is InvalidReq;
            }
        }

        internal class Receiver : AtLeastOnceDeliveryReceiveActor
        {
            private readonly IDictionary<string, ActorPath> _destinations;
            private readonly bool _isAsync;
            private readonly ILoggingAdapter _log;
            private readonly IActorRef _testActor;
            private IActorRef _lastSnapshotAskedForBy;
            private readonly string _name;

            public Receiver(IActorRef testActor, string name, TimeSpan redeliverInterval, int warn,
                int redeliveryBurstLimit, bool isAsync, IDictionary<string, ActorPath> destinations)
                : base(new PersistenceSettings.AtLeastOnceDeliverySettings(redeliverInterval, redeliveryBurstLimit, warn, 100000))
            {
                _testActor = testActor;
                _name = name;
                _isAsync = isAsync;
                _destinations = destinations;
                _log = Context.GetLogger();


                Command<Req>(req =>
                {
                    if (string.IsNullOrEmpty(req.Payload)) Sender.Tell(InvalidReq.Instance);
                    else
                    {
                        char c = char.ToUpper(req.Payload[0]);
                        ActorPath destination = _destinations[c.ToString()];
                        if (_isAsync)
                            PersistAsync(new AcceptedReq(req.Payload, destination.ToString()), e =>
                            {
                                UpdateState(e);
                                Sender.Tell(ReqAck.Instance);
                            });
                        else
                            Persist(new AcceptedReq(req.Payload, destination.ToString()), e =>
                            {
                                UpdateState(e);
                                Sender.Tell(ReqAck.Instance);
                            });
                    }
                });
                Command<ActionAck>(ack =>
                {
                    _log.Debug("Sender got ack: {0}", ack.Id);
                    if (ConfirmDelivery(ack.Id))
                    {
                        if (_isAsync)
                        {
                            PersistAsync(new ReqDone(ack.Id), UpdateState);
                        }
                        else
                        {
                            Persist(new ReqDone(ack.Id), UpdateState);
                        }
                    }
                });

                Command<Boom>(boom =>
                {
                    _log.Debug("Boom!");
                    throw new Exception("boom");
                });

                Command<SaveSnap>(save =>
                {
                    _log.Debug("Save snapshot");
                    _lastSnapshotAskedForBy = Sender;
                    SaveSnapshot(new Snap(GetDeliverySnapshot()));
                });


                Command<SaveSnapshotSuccess>(succ =>
                {
                    _log.Debug("Save snapshot success!");
                    if (_lastSnapshotAskedForBy != null)
                        _lastSnapshotAskedForBy.Tell(succ);
                });

                Command<UnconfirmedWarning>(unconfirmedWarn =>
                {
                    _log.Debug("Sender got unconfirmed warning: unconfirmed deliveries count {0}",
                        unconfirmedWarn.UnconfirmedDeliveries.Count());
                    _testActor.Tell(unconfirmedWarn);
                });

                Recover<IEvt>(evt => UpdateState(evt));

                Recover<SnapshotOffer>(o =>
                {
                    var snap = (Snap) o.Snapshot;
                    SetDeliverySnapshot(snap.DeliverySnapshot);
                });
            }

            public override string PersistenceId
            {
                get { return _name; }
            }



            private void UpdateState(IEvt evt)
            {
                evt.Match()
                    .With<AcceptedReq>(a =>
                    {
                        _log.Debug("Deliver(destination, deliveryId => Action(deliveryId, {0})), recovering: {1}",
                            a.Payload, IsRecovering);
                        Deliver(ActorPath.Parse(a.DestinationPath), deliveryId => new Action(deliveryId, a.Payload));
                    })
                    .With<ReqDone>(r =>
                    {
                        _log.Debug("ConfirmDelivery({0}), recovering: {1}", r.Id, IsRecovering);
                        ConfirmDelivery(r.Id);
                    });
            }
        }

        [Serializable]
        private sealed class Req
        {
            public Req(string payload)
            {
                Payload = payload;
            }

            public string Payload { get; private set; }
        }

        [Serializable]
        private sealed class ReqAck
        {
            public static readonly ReqAck Instance = new ReqAck();

            private ReqAck()
            {
            }

            public override bool Equals(object obj)
            {
                return obj is ReqAck;
            }
        }

        [Serializable]
        private sealed class ReqDone : IEvt, IEquatable<ReqDone>
        {
            public ReqDone(long id)
            {
                Id = id;
            }

            public long Id { get; private set; }

            public bool Equals(ReqDone other)
            {
                return other != null && other.Id == Id;
            }
        }

        [Serializable]
        private sealed class SaveSnap
        {
            public static readonly SaveSnap Instance = new SaveSnap();
        }

        [Serializable]
        private sealed class Snap
        {
            public Snap(AtLeastOnceDeliverySnapshot deliverySnapshot)
            {
                DeliverySnapshot = deliverySnapshot;
            }

            public AtLeastOnceDeliverySnapshot DeliverySnapshot { get; private set; }
        }

        private class Unreliable : ReceiveActor
        {
            private int _count;

            public Unreliable(int dropMod, IActorRef target)
            {
                Receive<object>(message =>
                {
                    _count++;
                    if (_count%dropMod != 0)
                    {
                        target.Forward(message);
                    }
                    return true;
                });
            }
        }

        private class DeliverToStarSelection : AtLeastOnceDeliveryReceiveActor
        {
            private readonly string _name;

            public DeliverToStarSelection(string name)
            {
                _name = name;
                Command<object>(message =>
                {
                    // this is not supported currently, so expecting exception
                    try
                    {
                        Deliver(Context.ActorSelection("*"), id => string.Format("{0}{1}", message, id));
                    }
                    catch (Exception ex)
                    {
                        Sender.Tell(ex.Message);
                    }
                });

                Recover<object>(message => {});
            }

            public override string PersistenceId
            {
                get { return _name; }
            }
        }

        #endregion

        public AtLeastOnceDeliveryReceiveActorSpec()
            : base(Configuration("AtLeastOnceDeliveryReceiveActorSpec"))
        {
        }

        [Fact]
        public void PersistentReceive_must_deliver_messages_in_order_when_nothing_is_lost()
        {
            TestProbe probe = CreateTestProbe();
            var destinations = new Dictionary<string, ActorPath>
            {
                {"A", Sys.ActorOf(Props.Create(() => new Destination(probe.Ref))).Path}
            };
            IActorRef sender =
                Sys.ActorOf(
                    Props.Create(
                        () =>
                            new Receiver(TestActor, Name, TimeSpan.FromMilliseconds(500), 5, 1000, false, destinations)),
                    Name);

            sender.Tell(new Req("a"));
            ExpectMsg(ReqAck.Instance);
            probe.ExpectMsg<Action>(a => a.Id == 1 && a.Payload == "a");
            probe.ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void PersistentReceive_must_not_allow_using_ActorSelection_with_wildcards()
        {
            Sys.ActorOf(Props.Create(() => new DeliverToStarSelection(Name))).Tell("anything, really.");
            ExpectMsg<string>().Contains("not supported").ShouldBeTrue();
        }


        [Fact]
        public void PersistentReceive_must_redeliver_lost_messages()
        {
            TestProbe probe = CreateTestProbe();
            IActorRef dest = Sys.ActorOf(Props.Create(() => new Destination(probe.Ref)));
            var destinations = new Dictionary<string, ActorPath>
            {
                {"A", Sys.ActorOf(Props.Create(() => new Unreliable(3, dest))).Path}
            };
            IActorRef sender =
                Sys.ActorOf(
                    Props.Create(
                        () =>
                            new Receiver(TestActor, Name, TimeSpan.FromMilliseconds(500), 5, 1000, false, destinations)),
                    Name);

            sender.Tell(new Req("a-1"));
            ExpectMsg(ReqAck.Instance);
            probe.ExpectMsg<Action>(a => a.Id == 1 && a.Payload == "a-1");

            sender.Tell(new Req("a-2"));
            ExpectMsg(ReqAck.Instance);
            probe.ExpectMsg<Action>(a => a.Id == 2 && a.Payload == "a-2");

            sender.Tell(new Req("a-3"));
            sender.Tell(new Req("a-4"));
            ExpectMsg(ReqAck.Instance);
            ExpectMsg(ReqAck.Instance);
            // a-3 was lost ...
            probe.ExpectMsg<Action>(a => a.Id == 4 && a.Payload == "a-4");
            // ... and then redelivered
            probe.ExpectMsg<Action>(a => a.Id == 3 && a.Payload == "a-3");
            probe.ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void PersistentReceive_must_redeliver_lost_messages_after_restart()
        {
            TestProbe probe = CreateTestProbe();
            IActorRef dest = Sys.ActorOf(Props.Create(() => new Destination(probe.Ref)));
            var destinations = new Dictionary<string, ActorPath>
            {
                {"A", Sys.ActorOf(Props.Create(() => new Unreliable(3, dest))).Path}
            };
            IActorRef sender =
                Sys.ActorOf(
                    Props.Create(
                        () =>
                            new Receiver(TestActor, Name, TimeSpan.FromMilliseconds(500), 5, 1000, false, destinations)),
                    Name);

            sender.Tell(new Req("a-1"));
            ExpectMsg(ReqAck.Instance);
            probe.ExpectMsg<Action>(a => a.Id == 1 && a.Payload == "a-1");

            sender.Tell(new Req("a-2"));
            ExpectMsg(ReqAck.Instance);
            probe.ExpectMsg<Action>(a => a.Id == 2 && a.Payload == "a-2");

            sender.Tell(new Req("a-3"));
            sender.Tell(new Req("a-4"));
            ExpectMsg(ReqAck.Instance);
            ExpectMsg(ReqAck.Instance);
            // a-3 was lost ...
            probe.ExpectMsg<Action>(a => a.Id == 4 && a.Payload == "a-4");
            // ... trigger restart ...
            sender.Tell(Boom.Instance);
            // ... and then redeliver
            probe.ExpectMsg<Action>(a => a.Id == 3 && a.Payload == "a-3");

            sender.Tell(new Req("a-5"));
            ExpectMsg(ReqAck.Instance);
            probe.ExpectMsg<Action>(a => a.Id == 5 && a.Payload == "a-5");

            probe.ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void
            PersistentReceive_must_resend_replayed_deliveries_with_an_initially_in_order_strategy_before_delivering_fresh_messages
            ()
        {
            TestProbe probe = CreateTestProbe();
            IActorRef dest = Sys.ActorOf(Props.Create(() => new Destination(probe.Ref)));
            var destinations = new Dictionary<string, ActorPath>
            {
                {"A", Sys.ActorOf(Props.Create(() => new Unreliable(2, dest))).Path}
            };
            IActorRef sender =
                Sys.ActorOf(
                    Props.Create(
                        () =>
                            new Receiver(TestActor, Name, TimeSpan.FromMilliseconds(500), 5, 1000, false, destinations)),
                    Name);

            sender.Tell(new Req("a-1"));
            ExpectMsg(ReqAck.Instance);
            probe.ExpectMsg<Action>(a => a.Id == 1 && a.Payload == "a-1");

            sender.Tell(new Req("a-2"));
            ExpectMsg(ReqAck.Instance);
            // a-2 was lost

            sender.Tell(new Req("a-3"));
            ExpectMsg(ReqAck.Instance);
            probe.ExpectMsg<Action>(a => a.Id == 3 && a.Payload == "a-3");

            sender.Tell(new Req("a-4"));
            ExpectMsg(ReqAck.Instance);
            // a-4 was lost

            // trigger restart
            sender.Tell(Boom.Instance);
            sender.Tell(new Req("a-5"));
            ExpectMsg(ReqAck.Instance);

            // and redeliver
            probe.ExpectMsg<Action>(a => a.Id == 2 && a.Payload == "a-2"); // redelivered
            // a-4 was redelivered but lost again
            probe.ExpectMsg<Action>(a => a.Id == 5 && a.Payload == "a-5"); // redelivered
            //FIXME: expression below works, just for some reason won't fit in 10 sec. interval
            probe.ExpectMsg<Action>(a => a.Id == 4 && a.Payload == "a-4", TimeSpan.FromSeconds(20));
                // redelivered, 3th time

            probe.ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void PersistentReceive_must_restore_state_from_snapshot()
        {
            TestProbe probe = CreateTestProbe();
            IActorRef dest = Sys.ActorOf(Props.Create(() => new Destination(probe.Ref)));
            var destinations = new Dictionary<string, ActorPath>
            {
                {"A", Sys.ActorOf(Props.Create(() => new Unreliable(3, dest))).Path}
            };
            IActorRef sender =
                Sys.ActorOf(
                    Props.Create(
                        () =>
                            new Receiver(TestActor, Name, TimeSpan.FromMilliseconds(500), 5, 1000, false, destinations)),
                    Name);

            sender.Tell(new Req("a-1"));
            ExpectMsg(ReqAck.Instance);
            probe.ExpectMsg<Action>(a => a.Id == 1 && a.Payload == "a-1");

            sender.Tell(new Req("a-2"));
            ExpectMsg(ReqAck.Instance);
            probe.ExpectMsg<Action>(a => a.Id == 2 && a.Payload == "a-2");

            sender.Tell(new Req("a-3"));
            sender.Tell(new Req("a-4"));
            sender.Tell(SaveSnap.Instance);
            ExpectMsg(ReqAck.Instance);
            ExpectMsg(ReqAck.Instance);
            // a-3 was lost

            probe.ExpectMsg<Action>(a => a.Id == 4 && a.Payload == "a-4");

            // after snapshot succeed
            ExpectMsg<SaveSnapshotSuccess>();
            // trigger restart
            sender.Tell(Boom.Instance);
            // and then redelivered
            probe.ExpectMsg<Action>(a => a.Id == 3 && a.Payload == "a-3");

            sender.Tell(new Req("a-5"));
            ExpectMsg(ReqAck.Instance);
            probe.ExpectMsg<Action>(a => a.Id == 5 && a.Payload == "a-5");

            probe.ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void PersistentReceive_must_warn_about_unconfirmed_messages()
        {
            TestProbe probeA = CreateTestProbe();
            TestProbe probeB = CreateTestProbe();

            var destinations = new Dictionary<string, ActorPath> {{"A", probeA.Ref.Path}, {"B", probeB.Ref.Path}};
            IActorRef sender =
                Sys.ActorOf(
                    Props.Create(
                        () =>
                            new Receiver(TestActor, Name, TimeSpan.FromMilliseconds(500), 3, 1000, false, destinations)),
                    Name);

            sender.Tell(new Req("a-1"));
            sender.Tell(new Req("b-1"));
            sender.Tell(new Req("b-2"));
            ExpectMsg(ReqAck.Instance);
            ExpectMsg(ReqAck.Instance);
            ExpectMsg(ReqAck.Instance);

            UnconfirmedDelivery[] unconfirmed = ReceiveWhile(TimeSpan.FromSeconds(3), x =>
                x is UnconfirmedWarning
                    ? ((UnconfirmedWarning) x).UnconfirmedDeliveries
                    : Enumerable.Empty<UnconfirmedDelivery>())
                .SelectMany(e => e).ToArray();

            ActorPath[] resultDestinations = unconfirmed.Select(x => x.Destination).Distinct().ToArray();
            resultDestinations.ShouldOnlyContainInOrder(probeA.Ref.Path, probeB.Ref.Path);
            object[] resultMessages = unconfirmed.Select(x => x.Message).Distinct().ToArray();
            resultMessages.ShouldOnlyContainInOrder(new Action(1, "a-1"), new Action(2, "b-1"), new Action(3, "b-2"));

            Sys.Stop(sender);
        }

        public void PersistentReceive_must_redeliver_many_lost_messages()
        {
            var probeA = CreateTestProbe();
            var probeB = CreateTestProbe();
            var probeC = CreateTestProbe();
            var destA = Sys.ActorOf(Props.Create(() => new Destination(probeA.Ref)), "destination-a");
            var destB = Sys.ActorOf(Props.Create(() => new Destination(probeB.Ref)), "destination-b");
            var destC = Sys.ActorOf(Props.Create(() => new Destination(probeC.Ref)), "destination-c");
            var destinations = new Dictionary<string, ActorPath>
            {
                { "A", Sys.ActorOf(Props.Create(() => new Unreliable(2, destA)), "unreliable-a").Path },
                { "B", Sys.ActorOf(Props.Create(() => new Unreliable(5, destB)), "unreliable-b").Path },
                { "C", Sys.ActorOf(Props.Create(() => new Unreliable(3, destC)), "unreliable-c").Path }
            };
            var sender = Sys.ActorOf(Props.Create(() => new Receiver(TestActor, Name, TimeSpan.FromMilliseconds(1000), 5, 1000, true, destinations)), Name);

            const int n = 100;
            var a = new string[n];
            var b = new string[n];
            var c = new string[n];
            for (int i = 0; i < n; i++)
            {
                var x = "a-" + i;
                a[i] = x;
                sender.Tell(new Req(x));
            }
            for (int i = 0; i < n; i++)
            {
                var x = "b-" + i;
                b[i] = x;
                sender.Tell(new Req(x));
            }
            for (int i = 0; i < n; i++)
            {
                var x = "c-" + i;
                c[i] = x;
                sender.Tell(new Req(x));
            }
            var deliverWithin = TimeSpan.FromSeconds(20);

            var resAarr = probeA.ReceiveN(n, deliverWithin).Cast<Action>().Select(x => x.Payload).ToArray();
            var resBarr = probeB.ReceiveN(n, deliverWithin).Cast<Action>().Select(x => x.Payload).ToArray();
            var resCarr = probeC.ReceiveN(n, deliverWithin).Cast<Action>().Select(x => x.Payload).ToArray();

            resAarr.Except(a).Any().ShouldBeFalse();
            resBarr.Except(b).Any().ShouldBeFalse();
            resCarr.Except(c).Any().ShouldBeFalse();
        }

        [Fact(Skip = "Racy on Azure DevOps")]
        public void PersistentReceive_must_limit_the_number_of_messages_redelivered_at_once()
        {
            var probe = CreateTestProbe();
            var probeA = CreateTestProbe();
            var dst = Sys.ActorOf(Props.Create(() => new Destination(probeA.Ref)));

            var destinations = new Dictionary<string, ActorPath>
            {
                {"A", Sys.ActorOf(Props.Create(() => new Unreliable(2, dst))).Path}
            };

            var sender =
                Sys.ActorOf(
                    Props.Create(
                        () =>
                            new Receiver(probe.Ref, Name, TimeSpan.FromMilliseconds(1000), 5, 2, true, destinations)),
                    Name);

            const int N = 10;
            for (int i = 1; i <= N; i++)
            {
                sender.Tell(new Req("a-" + i),  probe.Ref);
            }

            // initially all odd messages should go through
            for (int i = 1; i <= N; i = i+2)
            {
                probeA.ExpectMsg<Action>(a => a.Id == i && a.Payload == "a-" + i);
            }
            probeA.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            // at each redelivery round, 2 (even) messages are sent, the first goes through
            // without throttling, at each round half of the messages would go through
            var toDeliver = Enumerable.Range(1, N).Where(i => i%2 == 0).Select(i => (long)i).ToList();
            for (int i = 2; i <= N; i = i+2)
            {
                toDeliver.Remove(probeA.ExpectMsg<Action>().Id);
                probeA.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            }

            toDeliver.Count.ShouldBe(0);
        }
    }
}
