﻿//-----------------------------------------------------------------------
// <copyright file="SnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;

namespace Akka.Persistence.Snapshot
{
    /// <summary>
    /// Abstract snapshot store.
    /// </summary>
    public abstract class SnapshotStore : ActorBase
    {
        private readonly TaskContinuationOptions _continuationOptions = TaskContinuationOptions.ExecuteSynchronously;
        private readonly bool _publish;
        private readonly CircuitBreaker _breaker;
        private readonly ILoggingAdapter _log;

        /// <summary>
        /// Initializes a new instance of the <see cref="SnapshotStore"/> class.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the associated Persistence extension has not been used in current actor system context.
        /// </exception>
        protected SnapshotStore()
        {
            var extension = Persistence.Instance.Apply(Context.System);
            if (extension == null)
            {
                throw new ArgumentException("Couldn't initialize SnapshotStore instance, because associated Persistence extension has not been used in current actor system context.");
            }

            _publish = extension.Settings.Internal.PublishPluginCommands;
            var config = extension.ConfigFor(Self);
            _breaker = CircuitBreaker.Create(
                Context.System.Scheduler,
                config.GetInt("circuit-breaker.max-failures", 10),
                config.GetTimeSpan("circuit-breaker.call-timeout", TimeSpan.FromSeconds(10)),
                config.GetTimeSpan("circuit-breaker.reset-timeout", TimeSpan.FromSeconds(30)));
            
            _log = Context.GetLogger();
        }

        /// <inheritdoc/>
        protected sealed override bool Receive(object message)
        {
            return ReceiveSnapshotStore(message) || ReceivePluginInternal(message);
        }

        private bool ReceiveSnapshotStore(object message)
        {
            var senderPersistentActor = Sender; // Sender is PersistentActor
            var self = Self; //Self MUST BE CLOSED OVER here, or the code below will be subject to race conditions

            if (message is LoadSnapshot loadSnapshot)
            {
                if (loadSnapshot.Criteria == SnapshotSelectionCriteria.None)
                {
                    senderPersistentActor.Tell(new LoadSnapshotResult(null, loadSnapshot.ToSequenceNr));
                }
                else
                {
                    _breaker.WithCircuitBreaker(ct => LoadAsync(loadSnapshot.PersistenceId, loadSnapshot.Criteria.Limit(loadSnapshot.ToSequenceNr), ct))
                        .ContinueWith(t => (!t.IsFaulted && !t.IsCanceled)
                            ? new LoadSnapshotResult(t.Result, loadSnapshot.ToSequenceNr) as ISnapshotResponse
                            : new LoadSnapshotFailed(t.IsFaulted
                                    ? TryUnwrapException(t.Exception)
                                    : new OperationCanceledException("LoadAsync canceled, possibly due to timing out.")),
                            _continuationOptions)
                        .PipeTo(senderPersistentActor);
                }
            }
            else if (message is SaveSnapshot saveSnapshot)
            {
                var metadata = new SnapshotMetadata(saveSnapshot.Metadata.PersistenceId, saveSnapshot.Metadata.SequenceNr, saveSnapshot.Metadata.Timestamp == DateTime.MinValue ? DateTime.UtcNow : saveSnapshot.Metadata.Timestamp);

                _breaker.WithCircuitBreaker(ct => SaveAsync(metadata, saveSnapshot.Snapshot, ct))
                    .ContinueWith(t => (!t.IsFaulted && !t.IsCanceled)
                        ? new SaveSnapshotSuccess(metadata) as ISnapshotResponse
                        : new SaveSnapshotFailure(saveSnapshot.Metadata,
                            t.IsFaulted
                                ? TryUnwrapException(t.Exception)
                                : new OperationCanceledException("SaveAsync canceled, possibly due to timing out.", TryUnwrapException(t.Exception))),
                        _continuationOptions)
                    .PipeTo(self, senderPersistentActor);
            }
            else if (message is SaveSnapshotSuccess)
            {
                try
                {
                    ReceivePluginInternal(message);
                }
                finally
                {
                    senderPersistentActor.Tell(message);
                }
            }
            else if (message is SaveSnapshotFailure saveSnapshotFailure)
            {
                try
                {
                    ReceivePluginInternal(message);
                    _breaker.WithCircuitBreaker(ct => DeleteAsync(saveSnapshotFailure.Metadata, ct))
                        .ContinueWith(t =>
                        {
                            if(t.IsFaulted)
                                _log.Error(t.Exception, "DeleteAsync operation after SaveSnapshot failure failed.");
                            else if(t.IsCanceled)
                                _log.Error(t.Exception, t.Exception is not null
                                    ? "DeleteAsync operation after SaveSnapshot failure canceled."
                                    : "DeleteAsync operation after SaveSnapshot failure canceled, possibly due to timing out.");
                        }, TaskContinuationOptions.ExecuteSynchronously);
                }
                finally
                {
                    senderPersistentActor.Tell(message);
                }
            }
            else if (message is DeleteSnapshot deleteSnapshot)
            {
                var eventStream = Context.System.EventStream;
                _breaker.WithCircuitBreaker(ct => DeleteAsync(deleteSnapshot.Metadata, ct))
                    .ContinueWith(t => (!t.IsFaulted && !t.IsCanceled)
                                ? new DeleteSnapshotSuccess(deleteSnapshot.Metadata) as ISnapshotResponse
                                : new DeleteSnapshotFailure(deleteSnapshot.Metadata,
                                    t.IsFaulted
                                        ? TryUnwrapException(t.Exception)
                                        : new OperationCanceledException("DeleteAsync canceled, possibly due to timing out.")),
                                _continuationOptions)
                    .PipeTo(self, senderPersistentActor)
                    .ContinueWith(_ =>
                    {
                        if (_publish)
                            eventStream.Publish(message);
                    }, _continuationOptions);
            }
            else if (message is DeleteSnapshotSuccess)
            {
                try
                {
                    ReceivePluginInternal(message);
                }
                finally
                {
                    senderPersistentActor.Tell(message);
                }
            }
            else if (message is DeleteSnapshotFailure)
            {
                try
                {
                    ReceivePluginInternal(message);
                }
                finally
                {
                    senderPersistentActor.Tell(message);
                }
            }
            else if (message is DeleteSnapshots deleteSnapshots)
            {
                var eventStream = Context.System.EventStream;
                _breaker.WithCircuitBreaker(ct => DeleteAsync(deleteSnapshots.PersistenceId, deleteSnapshots.Criteria, ct))
                    .ContinueWith(t => (!t.IsFaulted && !t.IsCanceled)
                                ? new DeleteSnapshotsSuccess(deleteSnapshots.Criteria) as ISnapshotResponse
                                : new DeleteSnapshotsFailure(deleteSnapshots.Criteria,
                                    t.IsFaulted
                                        ? TryUnwrapException(t.Exception)
                                        : new OperationCanceledException("DeleteAsync canceled, possibly due to timing out.")),
                                _continuationOptions)
                    .PipeTo(self, senderPersistentActor)
                    .ContinueWith(_ =>
                    {
                        if (_publish)
                            eventStream.Publish(message);
                    }, _continuationOptions);
            }
            else if (message is DeleteSnapshotsSuccess)
            {
                try
                {
                    ReceivePluginInternal(message);
                }
                finally
                {
                    senderPersistentActor.Tell(message);
                }
            }
            else if (message is DeleteSnapshotsFailure)
            {
                try
                {
                    ReceivePluginInternal(message);
                }
                finally
                {
                    senderPersistentActor.Tell(message);
                }
            }
            else return false;
            return true;
        }

        private Exception TryUnwrapException(Exception e)
        {
            if (e is AggregateException aggregateException)
            {
                aggregateException = aggregateException.Flatten();
                if (aggregateException.InnerExceptions.Count == 1)
                    return aggregateException.InnerExceptions[0];
            }
            return e;
        }

        /// <summary>
        /// Plugin API: Asynchronously loads a snapshot.
        /// 
        /// This call is protected with a circuit-breaker
        /// </summary>
        /// <param name="persistenceId">Id of the persistent actor.</param>
        /// <param name="criteria">Selection criteria for loading.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to stop async operation</param>
        /// <returns>TBD</returns>
        protected abstract Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria, CancellationToken cancellationToken = default);

        /// <summary>
        /// Plugin API: Asynchronously saves a snapshot.
        /// 
        /// This call is protected with a circuit-breaker
        /// </summary>
        /// <param name="metadata">Snapshot metadata.</param>
        /// <param name="snapshot">Snapshot.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to stop async operation</param>
        /// <returns>TBD</returns>
        protected abstract Task SaveAsync(SnapshotMetadata metadata, object snapshot, CancellationToken cancellationToken = default);

        /// <summary>
        /// Plugin API: Deletes the snapshot identified by <paramref name="metadata"/>.
        /// 
        /// This call is protected with a circuit-breaker
        /// </summary>
        /// <param name="metadata">Snapshot metadata.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to stop async operation</param>
        /// <returns>TBD</returns>
        protected abstract Task DeleteAsync(SnapshotMetadata metadata, CancellationToken cancellationToken = default);

        /// <summary>
        /// Plugin API: Deletes all snapshots matching provided <paramref name="criteria"/>.
        /// 
        /// This call is protected with a circuit-breaker
        /// </summary>
        /// <param name="persistenceId">Id of the persistent actor.</param>
        /// <param name="criteria">Selection criteria for deleting.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to stop async operation</param>
        /// <returns>TBD</returns>
        protected abstract Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria, CancellationToken cancellationToken = default);

        /// <summary>
        /// Plugin API: Allows plugin implementers to use f.PipeTo(Self)
        /// and handle additional messages for implementing advanced features
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected virtual bool ReceivePluginInternal(object message)
        {
            return false;
        }
    }
}
