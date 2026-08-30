//-----------------------------------------------------------------------
// <copyright file="SnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
        private const TaskContinuationOptions ContinuationOptions = TaskContinuationOptions.ExecuteSynchronously;
        private readonly bool _publish;
        private readonly CircuitBreaker _breaker;
        private readonly ILoggingAdapter _log;
        private readonly IReadOnlyDictionary<string, object> _defaultHealthCheckTags;

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
            _defaultHealthCheckTags = new Dictionary<string, object>
            {
                { "snapshot-store", Self.Path.Name }
            };
        }
        
        /// <summary>
        /// Health check for the snapshot store.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the health check invocation.</param>
        /// <returns>A <see cref="PersistenceHealthCheckResult"/> with a health status and optional error message.</returns>
        public virtual Task<PersistenceHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            if(_breaker.IsHalfOpen)
                return Task.FromResult(new PersistenceHealthCheckResult(PersistenceHealthStatus.Degraded, 
                    $"Circuit breaker is half-open, some operations may be failing intermittently.", _breaker.LastCaughtException, _defaultHealthCheckTags));
            if(_breaker.IsOpen)
                return Task.FromResult(new PersistenceHealthCheckResult(PersistenceHealthStatus.Degraded, 
                    $"Circuit breaker is open, some operations may be failing intermittently.", _breaker.LastCaughtException,  _defaultHealthCheckTags));
            return Task.FromResult(new PersistenceHealthCheckResult(PersistenceHealthStatus.Healthy, "OK.", Data: _defaultHealthCheckTags));
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

            switch (message)
            {
                case LoadSnapshot loadSnapshot when loadSnapshot.Criteria.Equals(SnapshotSelectionCriteria.None):
                    senderPersistentActor.Tell(new LoadSnapshotResult(null, loadSnapshot.ToSequenceNr));
                    break;
                
                case LoadSnapshot loadSnapshot:
                    _breaker.WithCircuitBreaker(ct => LoadAsync(loadSnapshot.PersistenceId, loadSnapshot.Criteria.Limit(loadSnapshot.ToSequenceNr), ct))
                        .ContinueWith(t => (!t.IsFaulted && !t.IsCanceled)
                                ? new LoadSnapshotResult(t.Result, loadSnapshot.ToSequenceNr) as ISnapshotResponse
                                : new LoadSnapshotFailed(t.IsFaulted
                                    ? TryUnwrapException(t.Exception)
                                    : new OperationCanceledException("LoadAsync canceled, possibly due to timing out.")),
                            ContinuationOptions)
                        .PipeTo(senderPersistentActor);
                    break;
                
                case SaveSnapshot saveSnapshot:
                    var metadata = new SnapshotMetadata(
                        persistenceId: saveSnapshot.Metadata.PersistenceId,
                        sequenceNr: saveSnapshot.Metadata.SequenceNr,
                        timestamp: saveSnapshot.Metadata.Timestamp == DateTime.MinValue 
                            ? DateTime.UtcNow 
                            : saveSnapshot.Metadata.Timestamp);
                    _breaker.WithCircuitBreaker(ct => SaveAsync(metadata, saveSnapshot.Snapshot, ct))
                        .ContinueWith(t => (!t.IsFaulted && !t.IsCanceled)
                                ? new SaveSnapshotSuccess(metadata) as ISnapshotResponse
                                : new SaveSnapshotFailure(saveSnapshot.Metadata,
                                    t.IsFaulted
                                        ? TryUnwrapException(t.Exception)
                                        : new OperationCanceledException("SaveAsync canceled, possibly due to timing out.", TryUnwrapException(t.Exception))),
                            ContinuationOptions)
                        .PipeTo(self, senderPersistentActor);
                    break;
                
                case SaveSnapshotSuccess:
                    try
                    {
                        ReceivePluginInternal(message);
                    }
                    finally
                    {
                        senderPersistentActor.Tell(message);
                    }
                    break;
                
                case SaveSnapshotFailure saveSnapshotFailure:
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
                    break;
                
                case DeleteSnapshot deleteSnapshot:
                {
                    var eventStream = Context.System.EventStream;
                    _breaker.WithCircuitBreaker(ct => DeleteAsync(deleteSnapshot.Metadata, ct))
                        .ContinueWith(t => (!t.IsFaulted && !t.IsCanceled)
                                ? new DeleteSnapshotSuccess(deleteSnapshot.Metadata) as ISnapshotResponse
                                : new DeleteSnapshotFailure(deleteSnapshot.Metadata,
                                    t.IsFaulted
                                        ? TryUnwrapException(t.Exception)
                                        : new OperationCanceledException("DeleteAsync canceled, possibly due to timing out.")),
                            ContinuationOptions)
                        .PipeTo(self, senderPersistentActor)
                        .ContinueWith(_ =>
                        {
                            if (_publish)
                                eventStream.Publish(message);
                        }, ContinuationOptions);
                    break;
                }
                
                case DeleteSnapshotSuccess:
                    try
                    {
                        ReceivePluginInternal(message);
                    }
                    finally
                    {
                        senderPersistentActor.Tell(message);
                    }
                    break;
                
                case DeleteSnapshotFailure:
                    try
                    {
                        ReceivePluginInternal(message);
                    }
                    finally
                    {
                        senderPersistentActor.Tell(message);
                    }
                    break;
                
                case DeleteSnapshots deleteSnapshots:
                {
                    var eventStream = Context.System.EventStream;
                    _breaker.WithCircuitBreaker(ct => DeleteAsync(deleteSnapshots.PersistenceId, deleteSnapshots.Criteria, ct))
                        .ContinueWith(t => (!t.IsFaulted && !t.IsCanceled)
                                ? new DeleteSnapshotsSuccess(deleteSnapshots.Criteria) as ISnapshotResponse
                                : new DeleteSnapshotsFailure(deleteSnapshots.Criteria,
                                    t.IsFaulted
                                        ? TryUnwrapException(t.Exception)
                                        : new OperationCanceledException("DeleteAsync canceled, possibly due to timing out.")),
                            ContinuationOptions)
                        .PipeTo(self, senderPersistentActor)
                        .ContinueWith(_ =>
                        {
                            if (_publish)
                                eventStream.Publish(message);
                        }, ContinuationOptions);
                    break;
                }
                
                case DeleteSnapshotsSuccess:
                    try
                    {
                        ReceivePluginInternal(message);
                    }
                    finally
                    {
                        senderPersistentActor.Tell(message);
                    }
                    break;
                
                case DeleteSnapshotsFailure:
                    try
                    {
                        ReceivePluginInternal(message);
                    }
                    finally
                    {
                        senderPersistentActor.Tell(message);
                    }

                    break;
                case CheckSnapshotStoreHealth checkHealth:
                    var sender = Sender;
                    CheckHealthAsync(checkHealth.CancellationToken)
                        // PipeTo implementation no longer requires a closure, but better safe than sorry
                        .PipeTo(sender, 
                            success: result => new SnapshotStoreHealthCheckResponse(result),
                            failure: ex => new SnapshotStoreHealthCheckResponse(
                                new PersistenceHealthCheckResult(PersistenceHealthStatus.Unhealthy,
                                    "Encountered exception while performing health check",
                                    ex, _defaultHealthCheckTags)));
                    break;
                
                default:
                    return false;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Exception TryUnwrapException(Exception e)
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
        /// <param name="cancellationToken"><see cref="CancellationToken"/> used to signal cancelled snapshot operation</param>
        protected abstract Task<SelectedSnapshot> LoadAsync(
            string persistenceId, 
            SnapshotSelectionCriteria criteria,
            CancellationToken cancellationToken);

        /// <summary>
        /// Plugin API: Asynchronously saves a snapshot.
        /// 
        /// This call is protected with a circuit-breaker
        /// </summary>
        /// <param name="metadata">Snapshot metadata.</param>
        /// <param name="snapshot">Snapshot.</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> used to signal cancelled snapshot operation</param>
        protected abstract Task SaveAsync(
            SnapshotMetadata metadata,
            object snapshot,
            CancellationToken cancellationToken);

        /// <summary>
        /// Plugin API: Deletes the snapshot identified by <paramref name="metadata"/>.
        /// 
        /// This call is protected with a circuit-breaker
        /// </summary>
        /// <param name="metadata">Snapshot metadata.</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> used to signal cancelled snapshot operation</param>
        protected abstract Task DeleteAsync(SnapshotMetadata metadata, CancellationToken cancellationToken);

        /// <summary>
        /// Plugin API: Deletes all snapshots matching provided <paramref name="criteria"/>.
        /// 
        /// This call is protected with a circuit-breaker
        /// </summary>
        /// <param name="persistenceId">Id of the persistent actor.</param>
        /// <param name="criteria">Selection criteria for deleting.</param>
        /// <param name="cancellationToken"><see cref="CancellationToken"/> used to signal cancelled snapshot operation</param>
        protected abstract Task DeleteAsync(
            string persistenceId, 
            SnapshotSelectionCriteria criteria,
            CancellationToken cancellationToken);

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
