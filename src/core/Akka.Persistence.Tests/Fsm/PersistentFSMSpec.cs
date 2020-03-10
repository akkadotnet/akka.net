//-----------------------------------------------------------------------
// <copyright file="PersistentFSMSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Persistence.Fsm;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Configuration;
using Xunit;
using static Akka.Actor.FSMBase;

namespace Akka.Persistence.Tests.Fsm
{
    public class PersistentFSMSpec : PersistenceSpec
    {
        public PersistentFSMSpec() : base(Configuration("PersistentFSMSpec"))
        {
        }

        [Fact]
        public void PersistentFSM_must_has_function_as_regular_fsm()
        {
            var dummyReportActorRef = CreateTestProbe().Ref;
            var fsmRef = Sys.ActorOf(WebStoreCustomerFSM.Props(Name, dummyReportActorRef));

            Watch(fsmRef);
            fsmRef.Tell(new SubscribeTransitionCallBack(TestActor));

            var shirt = new Item("1", "Shirt", 59.99F);
            var shoes = new Item("2", "Shoes", 89.99F);
            var coat = new Item("3", "Coat", 119.99F);

            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(shirt));
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(shoes));
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(coat));
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new Buy());
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new Leave());

            var userState = ExpectMsg<CurrentState<IUserState>>();
            userState.FsmRef.Should().Be(fsmRef);
            userState.State.Should().Be(LookingAround.Instance);
            ExpectMsg<EmptyShoppingCart>();

            var transition1 = ExpectMsg<Transition<IUserState>>();
            transition1.FsmRef.Should().Be(fsmRef);
            transition1.From.Should().Be(LookingAround.Instance);
            transition1.To.Should().Be(Shopping.Instance);
            ExpectMsg<NonEmptyShoppingCart>().Items.Should().ContainInOrder(shirt);
            ExpectMsg<NonEmptyShoppingCart>().Items.Should().ContainInOrder(shirt, shoes);
            ExpectMsg<NonEmptyShoppingCart>().Items.Should().ContainInOrder(shirt, shoes, coat);

            var transition2 = ExpectMsg<Transition<IUserState>>();
            transition2.FsmRef.Should().Be(fsmRef);
            transition2.From.Should().Be(Shopping.Instance);
            transition2.To.Should().Be(Paid.Instance);
            ExpectMsg<NonEmptyShoppingCart>().Items.Should().ContainInOrder(shirt, shoes, coat);

            ExpectTerminated(fsmRef);
        }

        [Fact]
        public void PersistentFSM_must_has_function_as_regular_fsm_on_state_timeout()
        {
            var dummyReportActorRef = CreateTestProbe().Ref;
            var fsmRef = Sys.ActorOf(WebStoreCustomerFSM.Props(Name, dummyReportActorRef));

            Watch(fsmRef);
            fsmRef.Tell(new SubscribeTransitionCallBack(TestActor));

            var shirt = new Item("1", "Shirt", 59.99F);

            fsmRef.Tell(new AddItem(shirt));

            var userState = ExpectMsg<CurrentState<IUserState>>();
            userState.FsmRef.Should().Be(fsmRef);
            userState.State.Should().Be(LookingAround.Instance);

            var transition1 = ExpectMsg<Transition<IUserState>>();
            transition1.FsmRef.Should().Be(fsmRef);
            transition1.From.Should().Be(LookingAround.Instance);
            transition1.To.Should().Be(Shopping.Instance);

            Within(TimeSpan.FromSeconds(0.9), RemainingOrDefault, () =>
            {
                var transition2 = ExpectMsg<Transition<IUserState>>();
                transition2.FsmRef.Should().Be(fsmRef);
                transition2.From.Should().Be(Shopping.Instance);
                transition2.To.Should().Be(Inactive.Instance);
            });

            ExpectTerminated(fsmRef);
        }

        [Fact]
        public void PersistentFSM_must_recover_successfully_with_correct_state_data()
        {
            var dummyReportActorRef = CreateTestProbe().Ref;
            var fsmRef = Sys.ActorOf(WebStoreCustomerFSM.Props(Name, dummyReportActorRef));

            Watch(fsmRef);
            fsmRef.Tell(new SubscribeTransitionCallBack(TestActor));

            var shirt = new Item("1", "Shirt", 59.99F);
            var shoes = new Item("2", "Shoes", 89.99F);
            var coat = new Item("3", "Coat", 119.99F);

            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(shirt));
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(shoes));
            fsmRef.Tell(new GetCurrentCart());

            var userState1 = ExpectMsg<CurrentState<IUserState>>();
            userState1.FsmRef.Should().Be(fsmRef);
            userState1.State.Should().Be(LookingAround.Instance);
            ExpectMsg<EmptyShoppingCart>();

            var transition1 = ExpectMsg<Transition<IUserState>>();
            transition1.FsmRef.Should().Be(fsmRef);
            transition1.From.Should().Be(LookingAround.Instance);
            transition1.To.Should().Be(Shopping.Instance);
            ExpectMsg<NonEmptyShoppingCart>().Items.Should().ContainInOrder(shirt);
            ExpectMsg<NonEmptyShoppingCart>().Items.Should().ContainInOrder(shirt, shoes);

            fsmRef.Tell(PoisonPill.Instance);
            ExpectTerminated(fsmRef);

            var recoveredFsmRef = Sys.ActorOf(Props.Create(() => new WebStoreCustomerFSM(Name, dummyReportActorRef)));
            Watch(recoveredFsmRef);
            recoveredFsmRef.Tell(new SubscribeTransitionCallBack(TestActor));

            recoveredFsmRef.Tell(new GetCurrentCart());

            recoveredFsmRef.Tell(new AddItem(coat));
            recoveredFsmRef.Tell(new GetCurrentCart());

            recoveredFsmRef.Tell(new Buy());
            recoveredFsmRef.Tell(new GetCurrentCart());
            recoveredFsmRef.Tell(new Leave());

            var userState2 = ExpectMsg<CurrentState<IUserState>>();
            userState2.FsmRef.Should().Be(recoveredFsmRef);
            userState2.State.Should().Be(Shopping.Instance);
            ExpectMsg<NonEmptyShoppingCart>().Items.Should().ContainInOrder(shirt, shoes);

            ExpectMsg<NonEmptyShoppingCart>().Items.Should().ContainInOrder(shirt, shoes, coat);

            var transition2 = ExpectMsg<Transition<IUserState>>();
            transition2.FsmRef.Should().Be(recoveredFsmRef);
            transition2.From.Should().Be(Shopping.Instance);
            transition2.To.Should().Be(Paid.Instance);
            ExpectMsg<NonEmptyShoppingCart>().Items.Should().ContainInOrder(shirt, shoes, coat);

            ExpectTerminated(recoveredFsmRef);
        }

        [Fact]
        public void PersistentFSM_must_execute_the_defined_actions_following_successful_persistence_of_state_change()
        {
            var reportActorProbe = CreateTestProbe(Sys);
            var fsmRef = Sys.ActorOf(WebStoreCustomerFSM.Props(Name, reportActorProbe.Ref));

            Watch(fsmRef);
            fsmRef.Tell(new SubscribeTransitionCallBack(TestActor));

            var shirt = new Item("1", "Shirt", 59.99F);
            var shoes = new Item("2", "Shoes", 89.99F);
            var coat = new Item("3", "Coat", 119.99F);

            fsmRef.Tell(new AddItem(shirt));
            fsmRef.Tell(new AddItem(shoes));
            fsmRef.Tell(new AddItem(coat));
            fsmRef.Tell(new Buy());
            fsmRef.Tell(new Leave());

            var userState = ExpectMsg<CurrentState<IUserState>>();
            userState.FsmRef.Should().Be(fsmRef);
            userState.State.Should().Be(LookingAround.Instance);

            var transition1 = ExpectMsg<Transition<IUserState>>();
            transition1.FsmRef.Should().Be(fsmRef);
            transition1.From.Should().Be(LookingAround.Instance);
            transition1.To.Should().Be(Shopping.Instance);

            var transition2 = ExpectMsg<Transition<IUserState>>();
            transition2.FsmRef.Should().Be(fsmRef);
            transition2.From.Should().Be(Shopping.Instance);
            transition2.To.Should().Be(Paid.Instance);

            reportActorProbe.ExpectMsg<PurchaseWasMade>().Items.Should().ContainInOrder(shirt, shoes, coat);

            ExpectTerminated(fsmRef);
        }

        [Fact]
        public void PersistentFSM_must_execute_the_defined_actions_following_successful_persistence_of_FSM_stop()
        {
            var reportActorProbe = CreateTestProbe(Sys);
            var fsmRef = Sys.ActorOf(WebStoreCustomerFSM.Props(Name, reportActorProbe.Ref));

            Watch(fsmRef);
            fsmRef.Tell(new SubscribeTransitionCallBack(TestActor));

            var shirt = new Item("1", "Shirt", 59.99F);
            var shoes = new Item("2", "Shoes", 89.99F);
            var coat = new Item("3", "Coat", 119.99F);

            fsmRef.Tell(new AddItem(shirt));
            fsmRef.Tell(new AddItem(shoes));
            fsmRef.Tell(new AddItem(coat));
            fsmRef.Tell(new Leave());

            var userState = ExpectMsg<CurrentState<IUserState>>();
            userState.FsmRef.Should().Be(fsmRef);
            userState.State.Should().Be(LookingAround.Instance);

            var transition = ExpectMsg<Transition<IUserState>>();
            transition.FsmRef.Should().Be(fsmRef);
            transition.From.Should().Be(LookingAround.Instance);
            transition.To.Should().Be(Shopping.Instance);

            reportActorProbe.ExpectMsg<ShoppingCardDiscarded>();

            ExpectTerminated(fsmRef);
        }

        [Fact]
        public void PersistentFSM_must_recover_successfully_with_correct_state_timeout()
        {
            var dummyReportActorRef = CreateTestProbe().Ref;
            var fsmRef = Sys.ActorOf(WebStoreCustomerFSM.Props(Name, dummyReportActorRef));

            Watch(fsmRef);
            fsmRef.Tell(new SubscribeTransitionCallBack(TestActor));

            var shirt = new Item("1", "Shirt", 59.99F);

            fsmRef.Tell(new AddItem(shirt));

            var userState1 = ExpectMsg<CurrentState<IUserState>>();
            userState1.FsmRef.Should().Be(fsmRef);
            userState1.State.Should().Be(LookingAround.Instance);

            var transition1 = ExpectMsg<Transition<IUserState>>();
            transition1.FsmRef.Should().Be(fsmRef);
            transition1.From.Should().Be(LookingAround.Instance);
            transition1.To.Should().Be(Shopping.Instance);

            ExpectNoMsg(TimeSpan.FromSeconds(0.6)); // arbitrarily chosen delay, less than the timeout, before stopping the FSM
            fsmRef.Tell(PoisonPill.Instance);
            ExpectTerminated(fsmRef);

            var recoveredFsmRef = Sys.ActorOf(Props.Create(() => new WebStoreCustomerFSM(Name, dummyReportActorRef)));
            Watch(recoveredFsmRef);
            recoveredFsmRef.Tell(new SubscribeTransitionCallBack(TestActor));

            var userState2 = ExpectMsg<CurrentState<IUserState>>();
            userState2.FsmRef.Should().Be(recoveredFsmRef);
            userState2.State.Should().Be(Shopping.Instance);

            Within(TimeSpan.FromSeconds(0.9), RemainingOrDefault, () =>
            {
                var transition2 = ExpectMsg<Transition<IUserState>>();
                transition2.FsmRef.Should().Be(recoveredFsmRef);
                transition2.From.Should().Be(Shopping.Instance);
                transition2.To.Should().Be(Inactive.Instance);
            });

            ExpectNoMsg(TimeSpan.FromSeconds(0.6)); // arbitrarily chosen delay, less than the timeout, before stopping the FSM
            recoveredFsmRef.Tell(PoisonPill.Instance);
            ExpectTerminated(recoveredFsmRef);

            var recoveredFsmRef2 = Sys.ActorOf(Props.Create(() => new WebStoreCustomerFSM(Name, dummyReportActorRef)));
            Watch(recoveredFsmRef2);
            recoveredFsmRef2.Tell(new SubscribeTransitionCallBack(TestActor));

            var userState3 = ExpectMsg<CurrentState<IUserState>>();
            userState3.FsmRef.Should().Be(recoveredFsmRef2);
            userState3.State.Should().Be(Inactive.Instance);
            ExpectTerminated(recoveredFsmRef2);
        }

        [Fact]
        public void PersistentFSM_must_not_trigger_onTransition_for_stay()
        {
            var probe = CreateTestProbe(Sys);
            var fsmRef = Sys.ActorOf(SimpleTransitionFSM.Props(Name, probe.Ref));

            probe.ExpectMsg("LookingAround -> LookingAround", 3.Seconds()); // caused by initialize(), OK

            fsmRef.Tell("goto(the same state)"); // causes goto()
            probe.ExpectMsg("LookingAround -> LookingAround", 3.Seconds());

            fsmRef.Tell("stay");
            probe.ExpectNoMsg(3.Seconds());
        }

        [Fact]
        public void PersistentFSM_must_not_persist_state_change_event_when_staying_in_the_same_state()
        {
            var dummyReportActorRef = CreateTestProbe().Ref;

            var fsmRef = Sys.ActorOf(WebStoreCustomerFSM.Props(Name, dummyReportActorRef));
            Watch(fsmRef);

            var shirt = new Item("1", "Shirt", 59.99F);
            var shoes = new Item("2", "Shoes", 89.99F);
            var coat = new Item("3", "Coat", 119.99F);

            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(shirt));
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(shoes));
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(coat));
            fsmRef.Tell(new GetCurrentCart());

            ExpectMsg<EmptyShoppingCart>();

            ExpectMsg<NonEmptyShoppingCart>().Items.Should().ContainInOrder(shirt);
            ExpectMsg<NonEmptyShoppingCart>().Items.Should().ContainInOrder(shirt, shoes);
            ExpectMsg<NonEmptyShoppingCart>().Items.Should().ContainInOrder(shirt, shoes, coat);

            fsmRef.Tell(PoisonPill.Instance);
            ExpectTerminated(fsmRef);

            var persistentEventsStreamer = Sys.ActorOf(PersistentEventsStreamer.Props(Name, TestActor));

            ExpectMsg<ItemAdded>().Item.Should().Be(shirt);
            ExpectMsg<PersistentFSM.StateChangeEvent>();

            ExpectMsg<ItemAdded>().Item.Should().Be(shoes);
            ExpectMsg<PersistentFSM.StateChangeEvent>();

            ExpectMsg<ItemAdded>().Item.Should().Be(coat);
            ExpectMsg<PersistentFSM.StateChangeEvent>();

            Watch(persistentEventsStreamer);
            persistentEventsStreamer.Tell(PoisonPill.Instance);
            ExpectTerminated(persistentEventsStreamer);
        }

        [Fact]
        public void PersistentFSM_must_persist_snapshot()
        {
            var dummyReportActorRef = CreateTestProbe().Ref;

            var fsmRef = Sys.ActorOf(WebStoreCustomerFSM.Props(Name, dummyReportActorRef));
            Watch(fsmRef);

            var shirt = new Item("1", "Shirt", 59.99F);
            var shoes = new Item("2", "Shoes", 89.99F);
            var coat = new Item("3", "Coat", 119.99F);

            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(shirt));
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(shoes));
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(coat));
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new Buy());
            fsmRef.Tell(new GetCurrentCart());

            ExpectMsg<EmptyShoppingCart>();

            ExpectMsg<NonEmptyShoppingCart>().Items.Should().ContainInOrder(shirt);
            ExpectMsg<NonEmptyShoppingCart>().Items.Should().ContainInOrder(shirt, shoes);
            ExpectMsg<NonEmptyShoppingCart>().Items.Should().ContainInOrder(shirt, shoes, coat);

            ExpectMsg<NonEmptyShoppingCart>().Items.Should().ContainInOrder(shirt, shoes, coat);
            ExpectNoMsg(1.Seconds());

            fsmRef.Tell(PoisonPill.Instance);
            ExpectTerminated(fsmRef);

            var recoveredFsmRef = Sys.ActorOf(WebStoreCustomerFSM.Props(Name, dummyReportActorRef));
            recoveredFsmRef.Tell(new GetCurrentCart());
            ExpectMsg<NonEmptyShoppingCart>().Items.Should().ContainInOrder(shirt, shoes, coat);

            Watch(recoveredFsmRef);
            recoveredFsmRef.Tell(PoisonPill.Instance);
            ExpectTerminated(recoveredFsmRef);

            var persistentEventsStreamer = Sys.ActorOf(PersistentEventsStreamer.Props(Name, TestActor));

            ExpectMsg<SnapshotOffer>();

            Watch(persistentEventsStreamer);
            persistentEventsStreamer.Tell(PoisonPill.Instance);
            ExpectTerminated(persistentEventsStreamer);
        }

        [Fact]
        public void PersistentFSM_must_allow_cancelling_stateTimeout_by_issuing_forMax_null()
        {
            var probe = CreateTestProbe();

            var fsm = Sys.ActorOf(TimeoutFsm.Props(probe.Ref));
            probe.ExpectMsg<StateTimeout>();

            fsm.Tell(TimeoutFsm.OverrideTimeoutToInf.Instance);
            probe.ExpectMsg<TimeoutFsm.OverrideTimeoutToInf>();
            probe.ExpectNoMsg(1.Seconds());
        }

        [Fact]
        public void PersistentFSM_must_save_periodical_snapshots_if_enablesnapshotafter()
        {
            var sys2 = ActorSystem.Create("PersistentFsmSpec2", ConfigurationFactory.ParseString(@"
                akka.persistence.fsm.snapshot-after = 3
            ").WithFallback(Configuration("PersistentFSMSpec")));

            try
            {
                var probe = CreateTestProbe();
                var fsmRef = sys2.ActorOf(SnapshotFSM.Props(probe.Ref));

                fsmRef.Tell(1);
                fsmRef.Tell(2);
                fsmRef.Tell(3);
                // Needs to wait with expectMsg, before sending the next message to fsmRef.
                // Otherwise, stateData sent to this probe is already updated
                probe.ExpectMsg("SeqNo=3, StateData=List(3, 2, 1)");

                fsmRef.Tell("4x"); //changes the state to Persist4xAtOnce, also updates SeqNo although nothing is persisted
                fsmRef.Tell(10); //Persist4xAtOnce = persist 10, 4x times
                // snapshot-after = 3, but the SeqNo is not multiple of 3,
                // as saveStateSnapshot() is called at the end of persistent event "batch" = 4x of 10's.
            
                probe.ExpectMsg("SeqNo=8, StateData=List(10, 10, 10, 10, 3, 2, 1)");
            }
            finally
            {
                Shutdown(sys2);
            }
        }
        
        [Fact]
        public async Task PersistentFSM_must_pass_latest_statedata_to_AndThen()
        {
            var actor = Sys.ActorOf(Props.Create(() => new AndThenTestActor()));

            var response1 = await actor.Ask<AndThenTestActor.Data>(new AndThenTestActor.Command("Message 1")).ConfigureAwait(true);
            var response2 = await actor.Ask<AndThenTestActor.Data>(new AndThenTestActor.Command("Message 2")).ConfigureAwait(true);
            Assert.Equal("Message 1", response1.Value);
            Assert.Equal("Message 2", response2.Value);
        }

        internal class WebStoreCustomerFSM : PersistentFSM<IUserState, IShoppingCart, IDomainEvent>
        {
            public WebStoreCustomerFSM(string persistenceId, IActorRef reportActor)
            {
                PersistenceId = persistenceId;

                StartWith(LookingAround.Instance, new EmptyShoppingCart());

                When(LookingAround.Instance, (evt, state) =>
                {
                    if (evt.FsmEvent is AddItem addItem)
                    {
                        return GoTo(Shopping.Instance)
                            .Applying(new ItemAdded(addItem.Item))
                            .ForMax(TimeSpan.FromSeconds(1));
                    }
                    if (evt.FsmEvent is GetCurrentCart)
                    {
                        return Stay().Replying(evt.StateData);
                    }
                    return state;
                });

                When(Shopping.Instance, (evt, state) =>
                {
                    if (evt.FsmEvent is AddItem addItem)
                    {
                        return Stay().Applying(new ItemAdded(addItem.Item)).ForMax(TimeSpan.FromSeconds(1));
                    }

                    if (evt.FsmEvent is Buy)
                    {
                        return GoTo(Paid.Instance)
                            .Applying(new OrderExecuted())
                            .AndThen(cart =>
                            {
                                if (cart is NonEmptyShoppingCart nonShoppingCart)
                                {
                                    reportActor.Tell(new PurchaseWasMade(nonShoppingCart.Items));
                                    SaveStateSnapshot();
                                }
                                else if (cart is EmptyShoppingCart)
                                {
                                    SaveStateSnapshot();
                                }
                            });
                    }

                    if (evt.FsmEvent is Leave)
                    {
                        return Stop()
                            .Applying(new OrderDiscarded())
                            .AndThen(cart =>
                            {
                                reportActor.Tell(new ShoppingCardDiscarded());
                                SaveStateSnapshot();
                            });
                    }

                    if (evt.FsmEvent is GetCurrentCart)
                    {
                        return Stay().Replying(evt.StateData);
                    }

                    if (evt.FsmEvent is StateTimeout)
                    {
                        return GoTo(Inactive.Instance).ForMax(TimeSpan.FromSeconds(2));
                    }

                    return state;
                });

                When(Inactive.Instance, (evt, state) =>
                {
                    if (evt.FsmEvent is AddItem addItem)
                    {
                        return GoTo(Shopping.Instance)
                            .Applying(new ItemAdded(addItem.Item))
                            .ForMax(TimeSpan.FromSeconds(1));
                    }

                    if (evt.FsmEvent is StateTimeout)
                    {
                        return Stop()
                            .Applying(new OrderDiscarded())
                            .AndThen(cart => reportActor.Tell(new ShoppingCardDiscarded()));
                    }

                    return state;
                });

                When(Paid.Instance, (evt, state) =>
                {
                    if (evt.FsmEvent is Leave)
                    {
                        return Stop();
                    }

                    if (evt.FsmEvent is GetCurrentCart)
                    {
                        return Stay().Replying(evt.StateData);
                    }

                    return state;
                });
            }

            public override string PersistenceId { get; }

            internal static Props Props(string name, IActorRef dummyReportActorRef)
            {
                return Actor.Props.Create(() => new WebStoreCustomerFSM(name, dummyReportActorRef));
            }

            protected override IShoppingCart ApplyEvent(IDomainEvent evt, IShoppingCart cartBeforeEvent)
            {
                if (evt is ItemAdded itemAdded)
                {
                    return cartBeforeEvent.AddItem(itemAdded.Item);
                }

                if (evt is OrderExecuted)
                {
                    return cartBeforeEvent;
                }

                if (evt is OrderDiscarded)
                {
                    return cartBeforeEvent.Empty();
                }

                return cartBeforeEvent;
            }
        }
    }

    public class TimeoutFsm : PersistentFSM<TimeoutFsm.ITimeoutState, string, string>
    {
        public interface ITimeoutState: PersistentFSM.IFsmState
        {
            
        }

        public class Init : ITimeoutState
        {
            public static Init Instance { get; } = new Init();

            private Init() { }
            public override bool Equals(object obj) => !ReferenceEquals(obj, null) && obj is Init;
            public override int GetHashCode() => nameof(Init).GetHashCode();

            public string Identifier => "Init";
        }

        public class OverrideTimeoutToInf
        {
            public static readonly OverrideTimeoutToInf Instance = new OverrideTimeoutToInf();
            private OverrideTimeoutToInf() { }
        }

        public TimeoutFsm(IActorRef probe)
        {
            StartWith(Init.Instance, "");

            When(Init.Instance, (evt, state) =>
            {
                if (evt.FsmEvent is StateTimeout)
                {
                    probe.Tell(StateTimeout.Instance);
                    return Stay();
                }
                else if (evt.FsmEvent is OverrideTimeoutToInf)
                {
                    probe.Tell(OverrideTimeoutToInf.Instance);
                    return Stay().ForMax(TimeSpan.MaxValue);
                }

                return null;
            }, TimeSpan.FromMilliseconds(300));
        }

        public override string PersistenceId { get; } = "timeout-test";

        protected override string ApplyEvent(string e, string data)
        {
            return "whatever";
        }

        public static Props Props(IActorRef probe)
        {
            return Actor.Props.Create(() => new TimeoutFsm(probe));
        }
    }

    internal class SimpleTransitionFSM : PersistentFSM<IUserState, IShoppingCart, IDomainEvent>
    {
        public SimpleTransitionFSM(string persistenceId, IActorRef reportActor)
        {
            PersistenceId = persistenceId;

            StartWith(LookingAround.Instance, new EmptyShoppingCart());

            When(LookingAround.Instance, (evt, state) =>
            {
                if (evt.FsmEvent.Equals("stay"))
                {
                    return Stay();
                }
                return GoTo(LookingAround.Instance);
            });

            OnTransition((state, nextState) =>
            {
                reportActor.Tell($"{state} -> {nextState}");
            });
        }

        public override string PersistenceId { get; }

        protected override IShoppingCart ApplyEvent(IDomainEvent domainEvent, IShoppingCart currentData)
        {
            return currentData;
        }

        public static Props Props(string persistenceId, IActorRef reportActor)
        {
            return Actor.Props.Create(() => new SimpleTransitionFSM(persistenceId, reportActor));
        }
    }

    internal class PersistentEventsStreamer : PersistentActor
    {
        private readonly IActorRef _client;

        public PersistentEventsStreamer(string persistenceId, IActorRef client)
        {
            PersistenceId = persistenceId;
            _client = client;
        }

        public override string PersistenceId { get; }

        protected override bool ReceiveRecover(object message)
        {
            if (!(message is RecoveryCompleted))
            {
                _client.Tell(message);
            }

            return true;
        }

        protected override bool ReceiveCommand(object message)
        {
            return true;
        }

        public static Props Props(string persistenceId, IActorRef client)
        {
            return Actor.Props.Create(() => new PersistentEventsStreamer(persistenceId, client));
        }
    }

    #region Custome States

    public interface IUserState : PersistentFSM.IFsmState { }

    public class Shopping : IUserState
    {
        public static Shopping Instance { get; } = new Shopping();

        private Shopping() { }
        public override bool Equals(object obj) => !ReferenceEquals(obj, null) && obj is Shopping;
        public override int GetHashCode() => nameof(Shopping).GetHashCode();
        public override string ToString() => nameof(Shopping);

        public string Identifier => "Shopping";
    }

    public class Inactive : IUserState
    {
        public static Inactive Instance { get; } = new Inactive();

        private Inactive() { }
        public override bool Equals(object obj) => !ReferenceEquals(obj, null) && obj is Inactive;
        public override int GetHashCode() => nameof(Inactive).GetHashCode();
        public override string ToString() => nameof(Inactive);

        public string Identifier => "Inactive";
    }

    public class Paid : IUserState
    {
        public static Paid Instance { get; } = new Paid();

        private Paid() { }
        public override bool Equals(object obj) => !ReferenceEquals(obj, null) && obj is Paid;
        public override int GetHashCode() => nameof(Paid).GetHashCode();
        public override string ToString() => nameof(Paid);

        public string Identifier => "Paid";
    }

    public class LookingAround : IUserState
    {
        public static LookingAround Instance { get; } = new LookingAround();

        private LookingAround() { }
        public override bool Equals(object obj) => !ReferenceEquals(obj, null) && obj is LookingAround;
        public override int GetHashCode() => nameof(LookingAround).GetHashCode();
        public override string ToString() => nameof(LookingAround);

        public string Identifier => "Looking Around";
    }

    #endregion

    #region Customer states data

    internal class Item
    {
        public Item(string id, string name, float price)
        {
            Id = id;
            Name = name;
            Price = price;
        }

        public string Id { get; }
        public string Name { get; }
        public float Price { get; }

        #region Equals
        protected bool Equals(Item other)
        {
            return string.Equals(Id, other.Id) && string.Equals(Name, other.Name) && Price.Equals(other.Price);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Item)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Id != null ? Id.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Name != null ? Name.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Price.GetHashCode();
                return hashCode;
            }
        }
        #endregion
    }

    internal interface IShoppingCart
    {
        ICollection<Item> Items { get; set; }
        IShoppingCart AddItem(Item item);
        IShoppingCart Empty();
    }

    internal class EmptyShoppingCart : IShoppingCart
    {
        public IShoppingCart AddItem(Item item)
        {
            return new NonEmptyShoppingCart(item);
        }

        public IShoppingCart Empty()
        {
            return this;
        }

        public ICollection<Item> Items { get; set; }
    }

    internal class NonEmptyShoppingCart : IShoppingCart
    {
        public NonEmptyShoppingCart(Item item)
        {
            Items = new List<Item>();
            Items.Add(item);
        }

        public IShoppingCart AddItem(Item item)
        {
            Items.Add(item);
            return this;
        }

        public IShoppingCart Empty()
        {
            return new EmptyShoppingCart();
        }

        public ICollection<Item> Items { get; set; }
    }

    #endregion

    #region Customer commands

    internal interface ICommand
    {
    }

    internal class AddItem : ICommand
    {
        public AddItem(Item item)
        {
            Item = item;
        }

        public Item Item { get; private set; }
    }

    internal class Buy
    {
    }

    internal class Leave
    {
    }

    internal class GetCurrentCart : ICommand
    {
    }

    #endregion

    #region Customer domain events

    internal interface IDomainEvent
    {
    }

    internal class ItemAdded : IDomainEvent
    {
        public ItemAdded(Item item)
        {
            Item = item;
        }

        public Item Item { get; private set; }
    }

    internal class OrderExecuted : IDomainEvent
    {
    }

    internal class OrderDiscarded : IDomainEvent
    {
    }

    #endregion

    #region Side effects - report events to be sent to some

    internal interface IReportEvent
    {
    }

    internal class PurchaseWasMade : IReportEvent
    {
        public PurchaseWasMade(IEnumerable<Item> items)
        {
            Items = items;
        }

        public IEnumerable<Item> Items { get; }
    }

    internal class ShoppingCardDiscarded : IReportEvent
    {
    }

    #endregion

    public interface ISnapshotFSMState : PersistentFSM.IFsmState { }

    public class PersistSingleAtOnce : ISnapshotFSMState
    {
        public static PersistSingleAtOnce Instance { get; } = new PersistSingleAtOnce();
        private PersistSingleAtOnce() { }
        public string Identifier => "PersistSingleAtOnce";
    }

    public class Persist4xAtOnce : ISnapshotFSMState
    {
        public static Persist4xAtOnce Instance { get; } = new Persist4xAtOnce();
        private Persist4xAtOnce() { }
        public string Identifier => "Persist4xAtOnce";
    }
    
    public interface ISnapshotFSMEvent { }

    public class IntAdded : ISnapshotFSMEvent
    {
        public IntAdded(int i)
        {
            I = i;
        }
        
        public int I { get; }
    }

    internal class SnapshotFSM : PersistentFSM<ISnapshotFSMState, List<int>, ISnapshotFSMEvent>
    {
        public SnapshotFSM(IActorRef probe)
        {
            StartWith(PersistSingleAtOnce.Instance, null);

            When(PersistSingleAtOnce.Instance, (evt, state) =>
            {
                if (evt.FsmEvent is int i)
                {
                    return Stay().Applying(new IntAdded(i));
                }
                else if (evt.FsmEvent.Equals("4x"))
                {
                    return GoTo(Persist4xAtOnce.Instance);
                }
                else if (evt.FsmEvent is SaveSnapshotSuccess snap)
                {
                    probe.Tell($"SeqNo={snap.Metadata.SequenceNr}, StateData=List({string.Join(", ", StateData)})");
                    return Stay();
                }
                return Stay();
            });

            When(Persist4xAtOnce.Instance, (evt, state) =>
            {
                if (evt.FsmEvent is int i)
                {
                    return Stay().Applying(new IntAdded(i), new IntAdded(i), new IntAdded(i), new IntAdded(i));
                }
                else if (evt.FsmEvent is SaveSnapshotSuccess snap)
                {
                    probe.Tell($"SeqNo={snap.Metadata.SequenceNr}, StateData=List({string.Join(", ", StateData)})");
                    return Stay();
                }
                return Stay();
            });
        }

        public override string PersistenceId { get; } = "snapshot-fsm-test";

        protected override List<int> ApplyEvent(ISnapshotFSMEvent domainEvent, List<int> currentData)
        {
            if (domainEvent is IntAdded intAdded)
            {
                var list = new List<int>();
                list.Add(intAdded.I);
                if (currentData != null)
                    list.AddRange(currentData);
                return list;
            }
            return currentData;
        }
        
        public static Props Props(IActorRef probe)
        {
            return Actor.Props.Create(() => new SnapshotFSM(probe));
        }
    }
    #region AndThen receiving latest data
    
    public class AndThenTestActor : PersistentFSM<AndThenTestActor.IState, AndThenTestActor.Data, AndThenTestActor.IEvent>
    {
        public override string PersistenceId => "PersistentFSMSpec.AndThenTestActor";

        public AndThenTestActor()
        {
            StartWith(Init.Instance, new Data());
            When(Init.Instance, (evt, state) =>
            {
                switch (evt.FsmEvent)
                {
                    case Command cmd:
                        return Stay()
                            .Applying(new CommandReceived(cmd.Value))
                            .AndThen(data =>
                            {
                                // NOTE At this point, I'd expect data to be the value returned by Apply
                                Sender.Tell(data, Self);
                            });
                    default:
                        return Stay();
                }
            });
        }

        protected override Data ApplyEvent(IEvent domainEvent, Data currentData)
        {
            switch (domainEvent)
            {
                case CommandReceived cmd:
                    return new Data(cmd.Value);
                default:
                    return currentData;
            }
        }
    

        public interface IState : PersistentFSM.IFsmState
        {
        }

        public class Init : IState
        {
            public static readonly Init Instance = new Init();
            public string Identifier => "Init";
        }

        public class Data
        {
            public Data()
            {
            }

            public Data(string value)
            {
                Value = value;
            }

            public string Value { get; }
        }

        public interface IEvent
        {
        }

        public class CommandReceived : IEvent
        {
            public CommandReceived(string value)
            {
                Value = value;
            }

            public string Value { get; }
        }

        public class Command
        {
            public Command(string value)
            {
                Value = value;
            }

            public string Value { get; }
        }
    }
    #endregion
}
