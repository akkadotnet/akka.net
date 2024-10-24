//-----------------------------------------------------------------------
// <copyright file="TestSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading;

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
        private IConnectionInterceptor _connectionInterceptor = ConnectionInterceptors.Noop.Instance;

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

                case UseConnectionInterceptor use:
                    _connectionInterceptor = use.Interceptor;
                    Sender.Tell(Ack.Instance);
                    return true;
                
                default:
                    return base.ReceivePluginInternal(message);
            }
        }

        protected override async Task SaveAsync(SnapshotMetadata metadata, object snapshot, CancellationToken cancellationToken = default)
        {
            await _connectionInterceptor.InterceptAsync();
            await _saveInterceptor.InterceptAsync(metadata.PersistenceId, ToSelectionCriteria(metadata));
            await base.SaveAsync(metadata, snapshot, cancellationToken);
        }

        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria, CancellationToken cancellationToken = default)
        {
            await _connectionInterceptor.InterceptAsync();
            await _loadInterceptor.InterceptAsync(persistenceId, criteria);
            return await base.LoadAsync(persistenceId, criteria, cancellationToken);
        }

        protected override async Task DeleteAsync(SnapshotMetadata metadata, CancellationToken cancellationToken = default)
        {
            await _connectionInterceptor.InterceptAsync();
            await _deleteInterceptor.InterceptAsync(metadata.PersistenceId, ToSelectionCriteria(metadata));
            await base.DeleteAsync(metadata, cancellationToken);
        }

        protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria, CancellationToken cancellationToken = default)
        {
            await _connectionInterceptor.InterceptAsync();
            await _deleteInterceptor.InterceptAsync(persistenceId, criteria);
            await base.DeleteAsync(persistenceId, criteria, cancellationToken);
        }

        static SnapshotSelectionCriteria ToSelectionCriteria(SnapshotMetadata metadata)
            => new(metadata.SequenceNr, metadata.Timestamp, metadata.SequenceNr, metadata.Timestamp);

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
        
        public sealed class UseConnectionInterceptor
        {
            public UseConnectionInterceptor(IConnectionInterceptor interceptor)
            {
                Interceptor = interceptor;
            }

            public IConnectionInterceptor Interceptor { get; }
        }
        
        public sealed class Ack
        {
            public static readonly Ack Instance = new();
        }

        internal class TestSnapshotStoreWrapper : ITestSnapshotStore
        {
            public TestSnapshotStoreWrapper(IActorRef actor)
            {
                _actor = actor;
            }

            private readonly IActorRef _actor;

            public SnapshotStoreSaveBehavior OnSave => new(new SnapshotStoreSaveBehaviorSetter(_actor));
            public SnapshotStoreLoadBehavior OnLoad => new(new SnapshotStoreLoadBehaviorSetter(_actor));
            public SnapshotStoreDeleteBehavior OnDelete => new(new SnapshotStoreDeleteBehaviorSetter(_actor));
            public SnapshotStoreConnectionBehavior OnConnect => new(new SnapshotStoreConnectionBehaviorSetter(_actor));
        }
    }
}
