namespace Akka.Persistence.TestKit
{
    using System.Threading.Tasks;
    using Actor;
    using Snapshot;

    public class TestSnapshotStore : MemorySnapshotStore
    {
        private ISnapshotStoreInterceptor _saveInterceptor;
        private ISnapshotStoreInterceptor _loadInterceptor;
        private ISnapshotStoreInterceptor _deleteInterceptor;

        protected override bool ReceivePluginInternal(object message)
        {
            switch (message)
            {
                case UseSaveInterceptor use:
                    _saveInterceptor = use.Interceptor;
                    return true;

                case UseLoadInterceptor use:
                    _loadInterceptor = use.Interceptor;
                    return true;

                case UseDeleteInterceptor use:
                    _deleteInterceptor = use.Interceptor;
                    return true;

                default:
                    return base.ReceivePluginInternal(message);
            }
        }

        protected override async Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            await _saveInterceptor.Intercept(metadata.PersistenceId, ToSelectionCriteria(metadata));
            await base.SaveAsync(metadata, snapshot);
        }

        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            await _loadInterceptor.Intercept(persistenceId, criteria);
            return await base.LoadAsync(persistenceId, criteria);
        }

        protected override async Task DeleteAsync(SnapshotMetadata metadata)
        {
            await _deleteInterceptor.Intercept(metadata.PersistenceId, ToSelectionCriteria(metadata));
            await base.DeleteAsync(metadata);
        }

        protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            await _deleteInterceptor.Intercept(persistenceId, criteria);
            await base.DeleteAsync(persistenceId, criteria);
        }

        static SnapshotSelectionCriteria ToSelectionCriteria(SnapshotMetadata metadata)
            => new SnapshotSelectionCriteria(metadata.SequenceNr, metadata.Timestamp, metadata.SequenceNr,
                metadata.Timestamp);

        public static ITestSnapshotStore FromRef(IActorRef actor)
        {
            return new TestSnapshotStoreWrapper(actor);
        }

        public sealed class UseSaveInterceptor
        {
            public UseSaveInterceptor(ISnapshotStoreInterceptor interceptor)
            {
                Interceptor = interceptor;
            }

            public ISnapshotStoreInterceptor Interceptor { get; }
        }

        public sealed class UseLoadInterceptor
        {
            public UseLoadInterceptor(ISnapshotStoreInterceptor interceptor)
            {
                Interceptor = interceptor;
            }

            public ISnapshotStoreInterceptor Interceptor { get; }
        }

        public sealed class UseDeleteInterceptor
        {
            public UseDeleteInterceptor(ISnapshotStoreInterceptor interceptor)
            {
                Interceptor = interceptor;
            }

            public ISnapshotStoreInterceptor Interceptor { get; }
        }

        public sealed class Ack
        {
            public static readonly Ack Instance = new Ack();
        }

        internal class TestSnapshotStoreWrapper : ITestSnapshotStore
        {
            public TestSnapshotStoreWrapper(IActorRef actor)
            {
                _actor = actor;
            }

            private readonly IActorRef _actor;

            public SnapshotStoreSaveBehavior OnSave => new SnapshotStoreSaveBehavior(new SnapshotStoreSaveBehaviorSetter(_actor));
            public SnapshotStoreLoadBehavior OnLoad => new SnapshotStoreLoadBehavior(new SnapshotStoreLoadBehaviorSetter(_actor));
            public SnapshotStoreDeleteBehavior OnDelete => new SnapshotStoreDeleteBehavior(new SnapshotStoreDeleteBehaviorSetter(_actor));
        }
    }
}
