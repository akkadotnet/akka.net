//-----------------------------------------------------------------------
// <copyright file="NoSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// <summary>
        /// TBD
        /// </summary>
        public class NoSnapshotStoreException : Exception
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="NoSnapshotStoreException"/> class.
            /// </summary>
            public NoSnapshotStoreException()
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="NoSnapshotStoreException"/> class.
            /// </summary>
            /// <param name="message">The message that describes the error.</param>
            public NoSnapshotStoreException(string message) : base(message)
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="NoSnapshotStoreException"/> class.
            /// </summary>
            /// <param name="message">The message that describes the error.</param>
            /// <param name="innerException">The exception that is the cause of the current exception.</param>
            public NoSnapshotStoreException(string message, Exception innerException) : base(message, innerException)
            {
            }

#if SERIALIZATION
            /// <summary>
            /// Initializes a new instance of the <see cref="NoSnapshotStoreException"/> class.
            /// </summary>
            /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
            /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
            protected NoSnapshotStoreException(SerializationInfo info, StreamingContext context) : base(info, context)
            {
            }
#endif
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="criteria">TBD</param>
        /// <returns>TBD</returns>
        protected override Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            return Task.FromResult((SelectedSnapshot)null);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="metadata">TBD</param>
        /// <param name="snapshot">TBD</param>
        /// <exception cref="NoSnapshotStoreException">
        /// This exception is thrown when no snapshot store is configured.
        /// </exception>
        /// <returns>TBD</returns>
        protected override Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            return Flop();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="metadata">TBD</param>
        /// <exception cref="NoSnapshotStoreException">
        /// This exception is thrown when no snapshot store is configured.
        /// </exception>
        /// <returns>TBD</returns>
        protected override Task DeleteAsync(SnapshotMetadata metadata)
        {
            return Flop();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="criteria">TBD</param>
        /// <exception cref="NoSnapshotStoreException">
        /// This exception is thrown when no snapshot store is configured.
        /// </exception>
        /// <returns>TBD</returns>
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
