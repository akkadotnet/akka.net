using System;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence.Snapshot
{
    public abstract class SnapshotStore : ActorBase
    {
        private readonly PersistenceExtension _persistence;
        private readonly bool _publish;

        protected SnapshotStore()
        {
            _persistence = Context.System.GetExtension<PersistenceExtension>();
            if (_persistence == null)
            {
                throw new ArgumentException("Couldn't initialize SnapshotStore instance, because associated Persistance extension has not been used in current actor system context.");
            }

            _publish = _persistence.Settings.Internal.PublishPluginCommands;
        }

        protected override bool Receive(object message)
        {
            if (message is LoadSnapshot)
            {
                var msg = (LoadSnapshot) message;
                var sender = Sender;
                var task = LoadAsync(msg.PersistenceId, msg.Criteria.Limit(msg.ToSequenceNr));
                task.ContinueWith(t => sender.Tell(!t.IsFaulted
                    ? new LoadSnapshotResult(t.Result, msg.ToSequenceNr)
                    : new LoadSnapshotResult(null, msg.ToSequenceNr)));
            }
            else if (message is SaveSnapshot)
            {
                var msg = (SaveSnapshot)message;
                var sender = Sender;
                var metadata = new SnapshotMetadata(msg.Metadata.PersistenceId, msg.Metadata.SequenceNr, DateTime.UtcNow);
                SaveAsync(metadata, msg.Snapshot).ContinueWith(t =>
                    Self.Tell(!t.IsFaulted
                        ? (object) new SaveSnapshotSuccess(metadata)
                        : new SaveSnapshotFailure(msg.Metadata, t.Exception), sender));

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
                var msg = (DeleteSnapshots)message;
                Delete(msg.PersistenceId, msg.Criteria);
                if (_publish)
                {
                    Context.System.EventStream.Publish(message);
                }
            }
            else
            {
                return false;
            }

            return true;
        }

        protected abstract Task<SelectedSnapshot?> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria);
        protected abstract Task SaveAsync(SnapshotMetadata metadata, object snapshot);
        protected abstract void Saved(SnapshotMetadata metadata);
        protected abstract void Delete(SnapshotMetadata metadata);
        protected abstract void Delete(string persistenceId, SnapshotSelectionCriteria criteria);
    }
}