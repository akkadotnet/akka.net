//-----------------------------------------------------------------------
// <copyright file="TestSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using System.Threading.Tasks;
    using Actor;
    using Snapshot;

    /// <summary>
    ///     In-memory persistence snapshot store implementation which behavior could be controlled by interceptors.
    /// </summary>
    public class TestSnapshotStore : MemorySnapshotStore
    {
        private ISnapshotStoreInterceptor _saveInterceptor = SnapshotStoreInterceptors.Noop.Instance;
        private ISnapshotStoreInterceptor _loadInterceptor = SnapshotStoreInterceptors.Noop.Instance;
        private ISnapshotStoreInterceptor _deleteInterceptor = SnapshotStoreInterceptors.Noop.Instance;

        protected override bool ReceivePluginInternal(object message)
        {
            switch (message)
            {
                case UseSaveInterceptor use:
                    _saveInterceptor = use.Interceptor;
                    Sender.Tell(Ack.Instance);
                    return true;

                case UseLoadInterceptor use:
                    _loadInterceptor = use.Interceptor;
                    Sender.Tell(Ack.Instance);
                    return true;

                case UseDeleteInterceptor use:
                    _deleteInterceptor = use.Interceptor;
                    Sender.Tell(Ack.Instance);
                    return true;

                default:
                    return base.ReceivePluginInternal(message);
            }
        }

        protected override async Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            await _saveInterceptor.InterceptAsync(metadata.PersistenceId, ToSelectionCriteria(metadata));
            await base.SaveAsync(metadata, snapshot);
        }

        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            await _loadInterceptor.InterceptAsync(persistenceId, criteria);
            return await base.LoadAsync(persistenceId, criteria);
        }

        protected override async Task DeleteAsync(SnapshotMetadata metadata)
        {
            await _deleteInterceptor.InterceptAsync(metadata.PersistenceId, ToSelectionCriteria(metadata));
            await base.DeleteAsync(metadata);
        }

        protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            await _deleteInterceptor.InterceptAsync(persistenceId, criteria);
            await base.DeleteAsync(persistenceId, criteria);
        }

        static SnapshotSelectionCriteria ToSelectionCriteria(SnapshotMetadata metadata)
            => new SnapshotSelectionCriteria(metadata.SequenceNr, metadata.Timestamp, metadata.SequenceNr,
                metadata.Timestamp);

        /// <summary>
        ///     Create proxy object from snapshot store actor reference which can alter behavior of snapshot store.
        /// </summary>
        /// <remarks>
        ///     Snapshot store actor must be of <see cref="TestSnapshotStore"/> type.
        /// </remarks>
        /// <param name="actor">Journal actor reference.</param>
        /// <returns>Proxy object to control <see cref="TestSnapshotStore"/>.</returns>
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
