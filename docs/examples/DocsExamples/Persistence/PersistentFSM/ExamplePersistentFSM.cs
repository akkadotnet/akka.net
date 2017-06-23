using System;
using Akka.Persistence.Fsm;
using Akka.Actor;

namespace DocsExamples.Persistence.PersistentFSM
{
    public class ExamplePersistentFSM : PersistentFSM<UserState, IShoppingCart, IDomainEvent>
    {
        private readonly IActorRef _reportActor;

        public ExamplePersistentFSM(string persistenceId, IActorRef reportActor)
        {
            PersistenceId = persistenceId;
            _reportActor = reportActor;

            StartWith(UserState.LookingAround, new EmptyShoppingCart());

            When(UserState.LookingAround, (e, state) =>
            {
                if (e.FsmEvent is AddItem addItem)
                {
                    return GoTo(UserState.Shopping)
                        .Applying(new ItemAdded(addItem.Item))
                        .ForMax(TimeSpan.FromSeconds(1));
                }

                if (e.FsmEvent is GetCurrentCart)
                {
                    return Stay().Replying(e.StateData);
                }

                return state;
            });

            When(UserState.Shopping, (e, state) =>
            {
                if (e.FsmEvent is AddItem addItem)
                {
                    return Stay()
                        .Applying(new ItemAdded(addItem.Item))
                        .ForMax(TimeSpan.FromSeconds(1));
                }

                if (e.FsmEvent is Buy)
                {
                    return GoTo(UserState.Paid)
                        .Applying(new OrderExecuted())
                        .AndThen(cart =>
                        {
                            if (cart is NonEmptyShoppingCart)
                            {
                                _reportActor.Tell(new PurchaseWasMade());
                            }
                        });
                }

                if (e.FsmEvent is Leave)
                {
                    return Stop()
                        .Applying(new OrderDiscarded())
                        .AndThen(cart => _reportActor.Tell(new ShoppingCardDiscarded()));
                }

                if (e.FsmEvent is GetCurrentCart)
                {
                    return Stay().Replying(e.StateData);
                }

                if (e.FsmEvent is StateTimeout)
                {
                    return GoTo(UserState.Inactive).ForMax(TimeSpan.FromSeconds(2));
                }

                return state;
            });

            When(UserState.Inactive, (e, state) =>
            {
                if (e.FsmEvent is AddItem addItem)
                {
                    return GoTo(UserState.Shopping)
                        .Applying(new ItemAdded(addItem.Item))
                        .ForMax(TimeSpan.FromSeconds(1));
                }

                if (e.FsmEvent is GetCurrentCart)
                {
                    return Stop()
                        .Applying(new OrderDiscarded())
                        .AndThen(cart => _reportActor.Tell(new ShoppingCardDiscarded()));
                }

                return state;
            });

            When(UserState.Paid, (e, state) =>
            {
                if (e.FsmEvent is Leave)
                {
                    return Stop();
                }

                if (e.FsmEvent is GetCurrentCart)
                {
                    return Stay().Replying(e.StateData);
                }

                return state;
            });
        }

        protected override IShoppingCart ApplyEvent(IDomainEvent e, IShoppingCart data)
        {
            switch (e)
            {
                case ItemAdded itemAdded:
                    return data.AddItem(itemAdded.Item);
                case OrderExecuted orderedExecuted:
                    return data;
                case OrderDiscarded orderDiscarded:
                    return data.Empty();
                default:
                    return data;
            }
        }

        protected override void OnRecoveryCompleted()
        {

        }

        public override string PersistenceId { get; }
    }
}
