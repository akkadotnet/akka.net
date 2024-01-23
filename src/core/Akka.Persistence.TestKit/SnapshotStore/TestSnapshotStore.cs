//-----------------------------------------------------------------------
// <copyright file="TestSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Event;

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
        private readonly ILoggingAdapter _log = Context.GetLogger();
        
        public TestSnapshotStore(Config snapshotStoreConfig)
        {
            DebugEnabled = snapshotStoreConfig.GetBoolean("debug", false);
        }
        
        private bool DebugEnabled { get; }

        protected override bool ReceivePluginInternal(object message)
        {
            switch (message)
            {
                case UseSaveInterceptor use:
                    if(DebugEnabled)
                        _log.Info("Using save interceptor {0}", use.Interceptor.GetType().Name);
                    _saveInterceptor = use.Interceptor;
                    Sender.Tell(Ack.Instance);
                    return true;

                case UseLoadInterceptor use:
                    if(DebugEnabled)
                        _log.Info("Using load interceptor {0}", use.Interceptor.GetType().Name);
                    _loadInterceptor = use.Interceptor;
                    Sender.Tell(Ack.Instance);
                    return true;

                case UseDeleteInterceptor use:
                    if(DebugEnabled)
                        _log.Info("Using delete interceptor {0}", use.Interceptor.GetType().Name);
                    _deleteInterceptor = use.Interceptor;
                    Sender.Tell(Ack.Instance);
                    return true;

                default:
                    return base.ReceivePluginInternal(message);
            }
        }

        protected override async Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            if(DebugEnabled)
                _log.Info("Starting to intercept snapshot {0} saving using interceptor {1}", metadata, _saveInterceptor.GetType().Name);
            
            await _saveInterceptor.InterceptAsync(metadata.PersistenceId, ToSelectionCriteria(metadata));
            
            if(DebugEnabled)
                _log.Info("Completed intercept snapshot {0} saving using interceptor {1}", metadata, _saveInterceptor.GetType().Name);
            await base.SaveAsync(metadata, snapshot);
        }

        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            if(DebugEnabled)
                _log.Info("Starting to intercept snapshot {0} loading using interceptor {1}", persistenceId, _loadInterceptor.GetType().Name);
            await _loadInterceptor.InterceptAsync(persistenceId, criteria);
            
            if(DebugEnabled)
                _log.Info("Completed intercept snapshot {0} loading using interceptor {1}", persistenceId, _loadInterceptor.GetType().Name);
            
            return await base.LoadAsync(persistenceId, criteria);
        }

        protected override async Task DeleteAsync(SnapshotMetadata metadata)
        {
            if(DebugEnabled)
                _log.Info("Starting to intercept snapshot {0} deletion using interceptor {1}", metadata, _deleteInterceptor.GetType().Name);
            
            await _deleteInterceptor.InterceptAsync(metadata.PersistenceId, ToSelectionCriteria(metadata));
            
            if(DebugEnabled)
                _log.Info("Completed intercept snapshot {0} deletion using interceptor {1}", metadata, _deleteInterceptor.GetType().Name);
            
            await base.DeleteAsync(metadata);
        }

        protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            if(DebugEnabled)
                _log.Info("Starting to intercept snapshot {0} deletion using interceptor {1}", persistenceId, _deleteInterceptor.GetType().Name);
            
            await _deleteInterceptor.InterceptAsync(persistenceId, criteria);
            
            if(DebugEnabled)
                _log.Info("Completed intercept snapshot {0} deletion using interceptor {1}", persistenceId, _deleteInterceptor.GetType().Name);
            
            await base.DeleteAsync(persistenceId, criteria);
        }

        private static SnapshotSelectionCriteria ToSelectionCriteria(SnapshotMetadata metadata)
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
        }
    }
}
