//-----------------------------------------------------------------------
// <copyright file="SnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
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
                throw new ArgumentException(
                    "Couldn't initialize SnapshotStore instance, because associated Persistence extension has not been used in current actor system context.");
            }

            _publish = extension.Settings.Internal.PublishPluginCommands;
            var config = extension.ConfigFor(Self);
            _breaker = CircuitBreaker.Create(
                Context.System.Scheduler,
                config.GetInt("circuit-breaker.max-failures", 10),
                config.GetTimeSpan("circuit-breaker.call-timeout", TimeSpan.FromSeconds(10)),
                config.GetTimeSpan("circuit-breaker.reset-timeout", TimeSpan.FromSeconds(30)));
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
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            switch (message)
            {
                case LoadSnapshot loadSnapshot:

                    LoadSnapshotAsync(loadSnapshot, senderPersistentActor);
                    break;
                case SaveSnapshot saveSnapshot:
                    SaveSnapshotAsync(saveSnapshot, self, senderPersistentActor);
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
                        _breaker.WithCircuitBreaker((msg: saveSnapshotFailure, ss: this), state => state.ss.DeleteAsync(state.msg.Metadata));
                    }
                    finally
                    {
                        senderPersistentActor.Tell(message);
                    }

                    break;
                case DeleteSnapshot deleteSnapshot:
                    DeleteSnapshotAsync(deleteSnapshot, self, senderPersistentActor);
                    break;
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
                    DeleteSnapshotsAsync(deleteSnapshots, self, senderPersistentActor);
                    break;
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
                default:
                    return false;
            }
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            return true;
        }

        private async Task DeleteSnapshotsAsync(DeleteSnapshots deleteSnapshots, IActorRef self, IActorRef senderPersistentActor)
        {
            var eventStream = Context.System.EventStream;
            try
            {
                await _breaker.WithCircuitBreaker((msg: deleteSnapshots, ss: this),
                    state => state.ss.DeleteAsync(state.msg.PersistenceId, state.msg.Criteria));

                self.Tell(new DeleteSnapshotsSuccess(deleteSnapshots.Criteria), senderPersistentActor);
            }
            catch (Exception ex)
            {
                self.Tell(new DeleteSnapshotsFailure(deleteSnapshots.Criteria, ex), senderPersistentActor);
            }
            
            if (_publish)
                eventStream.Publish(deleteSnapshots);
        }

        private async Task DeleteSnapshotAsync(DeleteSnapshot deleteSnapshot, IActorRef self,
            IActorRef senderPersistentActor)
        {
            var eventStream = Context.System.EventStream;
            
            try
            {
                await _breaker.WithCircuitBreaker((msg: deleteSnapshot, ss: this),
                    state => state.ss.DeleteAsync(state.msg.Metadata));

                self.Tell(new DeleteSnapshotSuccess(deleteSnapshot.Metadata), senderPersistentActor);
            }
            catch (Exception ex)
            {
                self.Tell(new DeleteSnapshotFailure(deleteSnapshot.Metadata, ex), senderPersistentActor);
            }
            
            if (_publish)
                eventStream.Publish(deleteSnapshot);
        }

        private async Task SaveSnapshotAsync(SaveSnapshot saveSnapshot, IActorRef self, IActorRef senderPersistentActor)
        {
            var metadata = new SnapshotMetadata(saveSnapshot.Metadata.PersistenceId,
                saveSnapshot.Metadata.SequenceNr, DateTime.UtcNow);

            try
            {
                await _breaker.WithCircuitBreaker((msg: metadata, save: saveSnapshot, ss: this),
                    state => state.ss.SaveAsync(state.msg, state.save));
                self.Tell(new SaveSnapshotSuccess(metadata), senderPersistentActor);
            }
            catch (Exception ex)
            {
                self.Tell(new SaveSnapshotFailure(metadata, ex), senderPersistentActor);
            }
        }

        private async Task LoadSnapshotAsync(LoadSnapshot loadSnapshot, IActorRef senderPersistentActor)
        {
            if (loadSnapshot.Criteria == SnapshotSelectionCriteria.None)
            {
                senderPersistentActor.Tell(new LoadSnapshotResult(null, loadSnapshot.ToSequenceNr));
            }
            else
            {
                try
                {
                    var result = await _breaker.WithCircuitBreaker((msg: loadSnapshot, ss: this),
                        state => state.ss.LoadAsync(state.msg.PersistenceId,
                            state.msg.Criteria.Limit(state.msg.ToSequenceNr)));

                    senderPersistentActor.Tell(new LoadSnapshotResult(result, loadSnapshot.ToSequenceNr));
                }
                catch (Exception ex)
                {
                    senderPersistentActor.Tell(new LoadSnapshotFailed(ex));
                }
            }
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
        /// Plugin API: Asynchronously loads a snapshot.
        /// 
        /// This call is protected with a circuit-breaker
        /// </summary>
        /// <param name="persistenceId">Id of the persistent actor.</param>
        /// <param name="criteria">Selection criteria for loading.</param>
        /// <returns>TBD</returns>
        protected abstract Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria);

        /// <summary>
        /// Plugin API: Asynchronously saves a snapshot.
        /// 
        /// This call is protected with a circuit-breaker
        /// </summary>
        /// <param name="metadata">Snapshot metadata.</param>
        /// <param name="snapshot">Snapshot.</param>
        /// <returns>TBD</returns>
        protected abstract Task SaveAsync(SnapshotMetadata metadata, object snapshot);

        /// <summary>
        /// Plugin API: Deletes the snapshot identified by <paramref name="metadata"/>.
        /// 
        /// This call is protected with a circuit-breaker
        /// </summary>
        /// <param name="metadata">Snapshot metadata.</param>
        /// <returns>TBD</returns>
        protected abstract Task DeleteAsync(SnapshotMetadata metadata);

        /// <summary>
        /// Plugin API: Deletes all snapshots matching provided <paramref name="criteria"/>.
        /// 
        /// This call is protected with a circuit-breaker
        /// </summary>
        /// <param name="persistenceId">Id of the persistent actor.</param>
        /// <param name="criteria">Selection criteria for deleting.</param>
        /// <returns>TBD</returns>
        protected abstract Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria);

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