//-----------------------------------------------------------------------
// <copyright file="SnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Pattern;

namespace Akka.Persistence.Snapshot
{
    public abstract class SnapshotStore : ActorBase
    {
        private readonly TaskContinuationOptions _continuationOptions = TaskContinuationOptions.ExecuteSynchronously |
                                                                        TaskContinuationOptions.AttachedToParent;
        private readonly bool _publish;
        private readonly CircuitBreaker _breaker;

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
                config.GetInt("circuit-breaker.max-failures"),
                config.GetTimeSpan("circuit-breaker.call-timeout"),
                config.GetTimeSpan("circuit-breaker.reset-timeout"));
        }

        protected sealed override bool Receive(object message)
        {
            return ReceiveSnapshotStore(message) || ReceivePluginInternal(message);
        }

        private bool ReceiveSnapshotStore(object message)
        {
            if (message is LoadSnapshot)
            {
                var msg = (LoadSnapshot) message;

                _breaker.WithCircuitBreaker(() => LoadAsync(msg.PersistenceId, msg.Criteria.Limit(msg.ToSequenceNr)))
                    .ContinueWith(t => !t.IsFaulted && !t.IsCanceled
                        ? new LoadSnapshotResult(t.Result, msg.ToSequenceNr)
                        : new LoadSnapshotResult(null, msg.ToSequenceNr), _continuationOptions)
                    .PipeTo(Sender);
            }
            else if (message is SaveSnapshot)
            {
                var msg = (SaveSnapshot) message;
                var metadata = new SnapshotMetadata(msg.Metadata.PersistenceId, msg.Metadata.SequenceNr, DateTime.UtcNow);

                _breaker.WithCircuitBreaker(() => SaveAsync(metadata, msg.Snapshot))
                    .ContinueWith(t => !t.IsFaulted && !t.IsCanceled
                        ? (object) new SaveSnapshotSuccess(metadata)
                        : new SaveSnapshotFailure(msg.Metadata,
                            t.IsFaulted
                                ? TryUnwrapException(t.Exception)
                                : new OperationCanceledException("LoadAsync canceled, possibly due to timing out.")),
                        _continuationOptions)
                    .PipeTo(Self, Sender);

            }
            else if (message is SaveSnapshotSuccess)
            {
                try
                {
                    ReceivePluginInternal(message);
                }
                finally
                {
                    Sender.Tell(message); // Sender is PersistentActor
                }
            }
            else if (message is SaveSnapshotFailure)
            {
                var msg = (SaveSnapshotFailure) message;
                try
                {
                    ReceivePluginInternal(message);
                    _breaker.WithCircuitBreaker(() => DeleteAsync(msg.Metadata));
                }
                finally
                {
                    Sender.Tell(message); // Sender is PersistentActor
                }
            }
            else if (message is DeleteSnapshot)
            {
                var msg = (DeleteSnapshot) message;
                var eventStream = Context.System.EventStream;
                var sender = Sender;
                _breaker.WithCircuitBreaker(() => DeleteAsync(msg.Metadata))
                    .ContinueWith(
                        t =>
                            !t.IsFaulted && !t.IsCanceled
                                ? (object) new DeleteSnapshotSuccess(msg.Metadata)
                                : new DeleteSnapshotFailure(msg.Metadata,
                                    t.IsFaulted
                                        ? TryUnwrapException(t.Exception)
                                        : new OperationCanceledException(
                                            "DeleteAsync canceled, possibly due to timing out.")), _continuationOptions)
                    .PipeTo(Self, sender)
                    .ContinueWith(t =>
                    {
                        if (_publish) eventStream.Publish(message);
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
                    Sender.Tell(message); // Sender is PersistentActor
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
                    Sender.Tell(message); // Sender is PersistentActor
                }
            }
            else if (message is DeleteSnapshots)
            {
                var msg = (DeleteSnapshots) message;
                var eventStream = Context.System.EventStream;
                var sender = Sender;
                _breaker.WithCircuitBreaker(() => DeleteAsync(msg.PersistenceId, msg.Criteria))
                    .ContinueWith(
                        t =>
                            !t.IsFaulted && !t.IsCanceled
                                ? (object) new DeleteSnapshotsSuccess(msg.Criteria)
                                : new DeleteSnapshotsFailure(msg.Criteria,
                                    t.IsFaulted
                                        ? TryUnwrapException(t.Exception)
                                        : new OperationCanceledException(
                                            "DeleteAsync canceled, possibly due to timing out.")), _continuationOptions)
                    .PipeTo(Self, sender)
                    .ContinueWith(t =>
                    {
                        if (_publish) eventStream.Publish(message);
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
                    Sender.Tell(message); // Sender is PersistentActor
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
                    Sender.Tell(message); // Sender is PersistentActor
                }
            }
            else return false;
            return true;
        }

        private Exception TryUnwrapException(Exception e)
        {
            var aggregateException = e as AggregateException;
            if (aggregateException != null)
            {
                aggregateException = aggregateException.Flatten();
                if (aggregateException.InnerExceptions.Count == 1)
                    return aggregateException.InnerExceptions[0];
            }
            return e;
        }

        /// <summary>
        /// Asynchronously loads a snapshot.
        /// 
        /// This call is protected with a circuit-breaker
        /// </summary>
        protected abstract Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria);

        /// <summary>
        /// Asynchronously saves a snapshot.
        /// 
        /// This call is protected with a circuit-breaker
        /// </summary>
        protected abstract Task SaveAsync(SnapshotMetadata metadata, object snapshot);

        /// <summary>
        /// Deletes the snapshot identified by <paramref name="metadata"/>.
        /// 
        /// This call is protected with a circuit-breaker
        /// </summary>
        protected abstract Task DeleteAsync(SnapshotMetadata metadata);

        /// <summary>
        /// Deletes all snapshots matching provided <paramref name="criteria"/>.
        /// 
        /// This call is protected with a circuit-breaker
        /// </summary>
        protected abstract Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria);

        /// <summary>
        /// Allows plugin implementers to use <see cref="PipeToSupport.PipeTo{T}"/> <see cref="ActorBase.Self"/>
        /// and handle additional messages for implementing advanced features
        /// </summary>
        protected virtual bool ReceivePluginInternal(object message)
        {
            return false;
        }
    }
}

