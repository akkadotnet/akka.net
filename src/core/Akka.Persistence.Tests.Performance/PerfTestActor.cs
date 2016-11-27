using Akka.Actor;

namespace Akka.Persistence.Tests.Performance
{
    #region messages

    public interface ICommand { }

    public interface IDomainEvent { }

    public sealed class Init : ICommand
    {
        public static readonly Init Instance = new Init();
        private Init() { }
    }

    public sealed class Initialized
    {
        public static readonly Initialized Instance = new Initialized();
        private Initialized() { }
    }

    public sealed class Finish : ICommand
    {
        public static readonly Finish Instance = new Finish();
        private Finish() { }
    }

    public sealed class Finished
    {
        public static readonly Finished Instance = new Finished();
        private Finished() { }
    }

    public sealed class Store : ICommand
    {
        public readonly int Value;

        public Store(int value)
        {
            Value = value;
        }
    }

    public sealed class StoreAsync : ICommand
    {
        public readonly int Value;

        public StoreAsync(int value)
        {
            Value = value;
        }
    }
    public sealed class Stored : IDomainEvent
    {
        public readonly int Value;

        public Stored(int value)
        {
            Value = value;
        }
    }

    #endregion

    internal sealed class PerfTestActor : ReceivePersistentActor
    {
        public static Actor.Props Props(string persistenceId)
            => Actor.Props.Create(() => new PerfTestActor(persistenceId));

        public int State { get; private set; }
        public override string PersistenceId { get; }

        public PerfTestActor(string persistenceId)
        {
            PersistenceId = persistenceId;

            Command<Init>(_ => Persist(new Stored(0), UpdateState));
            Command<Store>(store => Persist(new Stored(store.Value), UpdateState));
            Command<StoreAsync>(store => PersistAsync(new Stored(store.Value), UpdateState));
            Command<Finish>(_ => DeferAsync(new Stored(0), s => Sender.Tell(Finished.Instance)));
        }

        private void UpdateState(Stored e)
        {
            State += e.Value;
        }
    }
}