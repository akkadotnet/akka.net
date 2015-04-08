//-----------------------------------------------------------------------
// <copyright file="SnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence.Snapshot
{
    public abstract class SnapshotStore : ActorBase
    {
        private readonly PersistenceExtension _extension;
        private readonly bool _publish;

        protected SnapshotStore()
        {
            _extension = Persistence.Instance.Apply(Context.System);
            if (_extension == null)
            {
                throw new ArgumentException("Couldn't initialize SnapshotStore instance, because associated Persistence extension has not been used in current actor system context.");
            }

            _publish = _extension.Settings.Internal.PublishPluginCommands;
        }

        protected override bool Receive(object message)
        {
            if (message is LoadSnapshot)
            {
                var msg = (LoadSnapshot)message;

                LoadAsync(msg.PersistenceId, msg.Criteria.Limit(msg.ToSequenceNr))
                    .ContinueWith(t => !t.IsFaulted
                    ? new LoadSnapshotResult(t.Result, msg.ToSequenceNr)
                    : new LoadSnapshotResult(null, msg.ToSequenceNr))
                    .PipeTo(Sender);
            }
            else if (message is SaveSnapshot)
            {
                var msg = (SaveSnapshot)message;
                var metadata = new SnapshotMetadata(msg.Metadata.PersistenceId, msg.Metadata.SequenceNr, DateTime.Now);

                SaveAsync(metadata, msg.Snapshot).ContinueWith(t => !t.IsFaulted
                        ? (object)new SaveSnapshotSuccess(metadata)
                        : new SaveSnapshotFailure(msg.Metadata, t.Exception))
                        .PipeTo(Self, Sender);

            }
            else if (message is SaveSnapshotSuccess)
            {
                var msg = (SaveSnapshotSuccess)message;
                Saved(msg.Metadata);
                Sender.Tell(message);       // Sender is PersistentActor
            }
            else if (message is SaveSnapshotFailure)
            {
                var msg = (SaveSnapshotFailure)message;
                Delete(msg.Metadata);
                Sender.Tell(message);       // Sender is PersistentActor
            }
            else if (message is DeleteSnapshot)
            {
                var msg = (DeleteSnapshot)message;
                Delete(msg.Metadata);

                if (_publish)
                {
                    Context.System.EventStream.Publish(message);
                }
            }
            else if (message is DeleteSnapshots)
            {
                var msg = (DeleteSnapshots) message;
                Delete(msg.PersistenceId, msg.Criteria);

                if (_publish)
                {
                    Context.System.EventStream.Publish(message);
                }
            }
            else return false;
            return true;
        }

        /// <summary>
        /// Asynchronously loads a snapshot.
        /// </summary>
        protected abstract Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria);

        /// <summary>
        /// Asynchronously saves a snapshot.
        /// </summary>
        protected abstract Task SaveAsync(SnapshotMetadata metadata, object snapshot);

        /// <summary>
        /// Called after successful saving a snapshot.
        /// </summary>
        protected abstract void Saved(SnapshotMetadata metadata);

        /// <summary>
        /// Deletes the snapshot identified by <paramref name="metadata"/>.
        /// </summary>
        protected abstract void Delete(SnapshotMetadata metadata);

        /// <summary>
        /// Deletes all snapshots matching provided <paramref name="criteria"/>.
        /// </summary>
        protected abstract void Delete(string persistenceId, SnapshotSelectionCriteria criteria);
    }
}
