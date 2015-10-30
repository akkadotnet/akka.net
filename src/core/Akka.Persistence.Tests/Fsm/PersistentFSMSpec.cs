using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Persistence.Fsm;
using Akka.TestKit;
using Akka.Util;
using Xunit;

namespace Akka.Persistence.Tests.Fsm
{
     public partial class PersistentFSMSpec : PersistenceSpec
    {
        private readonly Random _random = new Random();
        public PersistentFSMSpec()
            : base(PersistenceSpec.Configuration("inmem", "PersistentFSMSpec"))
        {
        }

        [Fact]
        public void PersistentFSM_should_has_function_as_regular_fsm()
        {
            var dummyReportActorRef = CreateTestProbe().Ref;

            var fsmRef = Sys.ActorOf(Props.Create<WebStoreCustomerFSM>(Name, dummyReportActorRef), Name);

            Watch(fsmRef);
            fsmRef.Tell(new FSMBase.SubscribeTransitionCallBack(TestActor));

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
            ExpectMsg<FSMBase.CurrentState<UserState>>(state => state.State == UserState.LookingAround);
            ExpectMsg<EmptyShoppingCart>();
            ExpectMsg<FSMBase.Transition<UserState>>(state => state.From == UserState.LookingAround);
            ExpectMsg<NonEmptyShoppingCart>();
            ExpectMsg<NonEmptyShoppingCart>();
            ExpectMsg<NonEmptyShoppingCart>();
            ExpectMsg<FSMBase.Transition<UserState>>();
            ExpectMsg<NonEmptyShoppingCart>();
            ExpectTerminated(fsmRef);
        }

        [Fact]
        public void PersistentFSM_should_has_function_as_regular_fsm_on_state_timeout()
        {
            var dummyReportActorRef = CreateTestProbe().Ref;

            var fsmRef = Sys.ActorOf(Props.Create<WebStoreCustomerFSM>(Name, dummyReportActorRef), Name);

            Watch(fsmRef);

            fsmRef.Tell(new FSMBase.SubscribeTransitionCallBack(TestActor));

            var shirt = new Item("1", "Shirt", 59.99F);

            fsmRef.Tell(new AddItem(shirt));
            ExpectMsg<FSMBase.CurrentState<UserState>>(state =>
            {
                return state.State == UserState.LookingAround;
                
            });
            
            ExpectMsg<FSMBase.Transition<UserState>>();

            Within(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.9), () =>
            {
                ExpectMsg<FSMBase.Transition<UserState>>();
                return true;
            });

            ExpectTerminated(fsmRef);
        }

        [Fact]
        public void PersistentFSM_should_recover_successfully_with_correct_state_data()
        {
            var dummyReportActorRef = CreateTestProbe().Ref;

            var fsmRef = Sys.ActorOf(Props.Create(()=> new WebStoreCustomerFSM(Name, dummyReportActorRef)));

            Watch(fsmRef);
            fsmRef.Tell(new FSMBase.SubscribeTransitionCallBack(TestActor));

            var shirt = new Item("1", "Shirt", 59.99F);
            var shoes = new Item("2", "Shoes", 89.99F);
            var coat = new Item("3", "Coat", 119.99F);

            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(shirt));
            fsmRef.Tell(new GetCurrentCart());
            fsmRef.Tell(new AddItem(shoes));
            fsmRef.Tell(new GetCurrentCart());


            //fsmRef.Tell(new AddItem(coat));
            //fsmRef.Tell(new GetCurrentCart());
            //fsmRef.Tell(new Buy());
            //fsmRef.Tell(new GetCurrentCart());
            //fsmRef.Tell(new Leave());


            ExpectMsg<FSMBase.CurrentState<UserState>>();
            ExpectMsg<EmptyShoppingCart>();
            ExpectMsg<FSMBase.Transition<UserState>>(state =>
            {
                return state.From == UserState.LookingAround;
                
            });
            ExpectMsg<NonEmptyShoppingCart>();
            ExpectMsg<NonEmptyShoppingCart>();

            fsmRef.Tell(PoisonPill.Instance);
            ExpectTerminated(fsmRef);

            var recoveredFsmRef = Sys.ActorOf(Props.Create(() => new WebStoreCustomerFSM(Name, dummyReportActorRef)));
            Watch(recoveredFsmRef);
            recoveredFsmRef.Tell(new FSMBase.SubscribeTransitionCallBack(TestActor));

            recoveredFsmRef.Tell(new GetCurrentCart());
            recoveredFsmRef.Tell(new AddItem(coat));
            recoveredFsmRef.Tell(new GetCurrentCart());
            recoveredFsmRef.Tell(new Buy());
            recoveredFsmRef.Tell(new GetCurrentCart());
            recoveredFsmRef.Tell(new Leave());

            ExpectMsg<FSMBase.CurrentState<UserState>>(state =>
            {
                return state.State == UserState.Shopping;
                
            });
            ExpectMsg<NonEmptyShoppingCart>();
            ExpectMsg<NonEmptyShoppingCart>();
            ExpectMsg<FSMBase.Transition<UserState>>();
            ExpectMsg<NonEmptyShoppingCart>();
            ExpectTerminated(recoveredFsmRef);
        }

        internal class WebStoreCustomerFSM : PersistentFSM<UserState, IShoppingCart, IDomainEvent>
        {
            private readonly string _persistenceId;
            private readonly IActorRef _reportActor;

            public WebStoreCustomerFSM(string persistenceId, IActorRef reportActor)
            {
                _persistenceId = persistenceId;
                _reportActor = reportActor;
                StartWith(UserState.LookingAround, new EmptyShoppingCart());

                When(UserState.LookingAround, (@event,state) =>
                {
                    if (@event.FsmEvent is AddItem)
                    {
                        var addItem = (AddItem) @event.FsmEvent;
                        return
                            GoTo(UserState.Shopping)
                                .Applying(new ItemAdded(addItem.Item)).ForMax(TimeSpan.FromSeconds(1));
                    }
                    if (@event.FsmEvent is GetCurrentCart)
                    {
                        return Stay().Replying(@event.StateData);
                    }
                    return state;
                });


                When(UserState.Shopping, (@event, state) =>
                {
                    if (@event.FsmEvent is AddItem)
                    {
                        var addItem = ((AddItem) @event.FsmEvent);
                        return Stay().Applying(new ItemAdded(addItem.Item)).ForMax(TimeSpan.FromSeconds(1));
                    }
                    if (@event.FsmEvent is Buy)
                    {
                        return
                            GoTo(UserState.Paid)
                                .Applying(new OrderExecuted())
                                .AndThen(cart =>
                                {
                                    if (cart is NonEmptyShoppingCart)
                                    {
                                        _reportActor.Tell(new PurchaseWasMade());
                                    }
                                });
                    }
                    if (@event.FsmEvent is Leave)
                    {
                        return Stop().Applying(new OrderDiscarded()).AndThen(cart =>
                        {
                            _reportActor.Tell(new ShoppingCardDiscarded());
                        });
                    }
                    if (@event.FsmEvent is GetCurrentCart)
                    {
                        return Stay().Replying(@event.StateData);
                    }
                    if (@event.FsmEvent is StateTimeout)
                    {
                        return GoTo(UserState.Inactive).ForMax(TimeSpan.FromSeconds(2));
                    }
                    return state;
                });


                When(UserState.Inactive, (@event, state) =>
                {
                    if (@event.FsmEvent is AddItem)
                    {
                        var addItem = (AddItem) @event.FsmEvent;
                        return
                            GoTo(UserState.Shopping)
                                .Applying(new ItemAdded(addItem.Item))
                                .ForMax(TimeSpan.FromSeconds(1));
                    }
                    if (@event.FsmEvent is StateTimeout)
                    {
                        //var addItem = ((AddItem)@event)
                        return Stop().Applying(new OrderDiscarded()).AndThen(cart =>
                        {
                            _reportActor.Tell(new ShoppingCardDiscarded());
                        });
                    }
                    return state;
                });

                When(UserState.Paid, (@event, state) =>
                {
                    if (@event.FsmEvent is Leave)
                    {
                        return Stop();
                    }
                    if (@event.FsmEvent is GetCurrentCart)
                    {
                        return Stay().Replying(@event.StateData);
                    }
                    return state;
                });
            }

            public override string PersistenceId { get { return  _persistenceId; }}



            //protected override IShoppingCart ApplyEvent(IDomainEvent state, IShoppingCart data)
            //{
            //    if (state is ItemAdded)
            //    {
            //        return data.AddItem(((ItemAdded)state).Item);
            //    }
            //    if (state is OrderExecuted)
            //    {
            //        return data;
            //    }
            //    if (state is OrderDiscarded)
            //    {
            //        return data.Empty();
            //    }
            //    return data;
            //}


            //protected override void OnRecoveryCompleted()
            //{
            //    //
            //}

            protected override IShoppingCart ApplyEvent(IDomainEvent e, IShoppingCart data)
            {
                if (e is ItemAdded)
                {
                    var itemAdded = (ItemAdded) e;
                    return data.AddItem(itemAdded.Item);
                }
                if (e is OrderExecuted)
                {
                    return data;
                }
                if (e is OrderDiscarded)
                {
                    return data.Empty();
                }

                return data;
            }
        }

    }

    #region Custome States
    internal enum UserState
    {
        Shopping,
        Inactive,
        Paid,
        LookingAround
    }

    #endregion

    #region Customer states data

    internal class Item
    {
        public string Id { get; }
        public string Name { get; }
        public float Price { get; }

        public Item(string id, string name, float price)
        {
            Id = id;
            Name = name;
            Price = price;
        }
    }

    internal interface IShoppingCart
    {
        IShoppingCart AddItem(Item item);
        IShoppingCart Empty();
    }

    internal class EmptyShoppingCart : IShoppingCart
    {
        public IShoppingCart AddItem(Item item)
        {
            return new NonEmptyShoppingCart();
        }

        public IShoppingCart Empty()
        {
            return this;
        }
    }

    internal class NonEmptyShoppingCart : IShoppingCart
    {
        public IShoppingCart AddItem(Item item)
        {
            return this;
        }

        public IShoppingCart Empty()
        {
            return new EmptyShoppingCart();
        }
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

        public Item Item { get; }
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

        public Item Item { get; }
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
    }
    internal class ShoppingCardDiscarded : IReportEvent
    {
    }
    #endregion
}
