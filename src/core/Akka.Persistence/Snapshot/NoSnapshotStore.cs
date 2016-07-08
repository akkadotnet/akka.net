//-----------------------------------------------------------------------
// <copyright file="NoSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Akka.Persistence.Snapshot
{
    /// <summary>
    /// Used as a default snapshot-store in case no other store was configured.
    /// 
    /// If a <see cref="PersistentActor"/> calls the <see cref="Eventsourced.SaveSnapshot(object)"/> method,
    /// and at the same time does not configure a specific snapshot-store to be used *and* no default snapshot-store
    /// is available, then the <see cref="NoSnapshotStore"/> will be used to signal a snapshot store failure.
    /// </summary>
    public sealed class NoSnapshotStore : SnapshotStore
    {
        public class NoSnapshotStoreException : Exception
        {
            public NoSnapshotStoreException()
            {
            }

            public NoSnapshotStoreException(string message) : base(message)
            {
            }

            public NoSnapshotStoreException(string message, Exception innerException) : base(message, innerException)
            {
            }
#if SERIALIZATION
            protected NoSnapshotStoreException(SerializationInfo info, StreamingContext context) : base(info, context)
            {
            }
#endif
        }

        protected override Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            return Task.FromResult((SelectedSnapshot)null);
        }

        protected override Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            return Flop();
        }

        protected override Task DeleteAsync(SnapshotMetadata metadata)
        {
            return Flop();
        }

        protected override Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            return Flop();
        }

        private Task Flop()
        {
            var promise = new TaskCompletionSource<object>();
            promise.SetException(new NoSnapshotStoreException("No snapshot store configured."));
            return promise.Task;
        }
    }
}