using System;
using Akka.Actor;

namespace Akka.Persistence
{

    /// <summary>
    /// Instructs a <see cref="PersistentView"/> to update itself. This will run a single incremental message replay 
    /// with all messages from the corresponding persistent id's journal that have not yet been consumed by the view.  
    /// To update a view with messages that have been written after handling this request, another <see cref="Update"/> 
    /// request must be sent to the view.
    /// </summary>
    [Serializable]
    public struct Update
    {
        public Update(bool isAwait = false, long replayMax = long.MaxValue)
            : this()
        {
            IsAwait = isAwait;
            ReplayMax = replayMax;
        }

        /// <summary>
        /// If `true`, processing of further messages sent to the view will be delayed 
        /// until the incremental message replay, triggered by this update request, completes. 
        /// If `false`, any message sent to the view may interleave with replayed <see cref="Persistent"/> message stream.
        /// </summary>
        public bool IsAwait { get; private set; }

        /// <summary>
        /// Maximum number of messages to replay when handling this update request. Defaults to <see cref="long.MaxValue"/> (i.e. no limit).
        /// </summary>
        public long ReplayMax { get; private set; }
    }

    /// <summary>
    /// A view replicates the persistent message stream of a <see cref="PersistentActorBase"/>. Implementation classes receive
    /// the message stream directly from the Journal. These messages can be processed to update internal state
    /// in order to maintain an (eventual consistent) view of the state of the corresponding persistent actor. A
    /// persistent view can also run on a different node, provided that a replicated journal is used.
    /// 
    /// Implementation classes refer to a persistent actors' message stream by implementing `persistenceId`
    /// with the corresponding (shared) identifier value.
    /// 
    /// Views can also store snapshots of internal state by calling [[autoUpdate]]. The snapshots of a view
    /// are independent of those of the referenced persistent actor. During recovery, a saved snapshot is offered
    /// to the view with a <see cref="SnapshotOffer"/> message, followed by replayed messages, if any, that are younger
    /// than the snapshot. Default is to offer the latest saved snapshot.
    /// 
    /// By default, a view automatically updates itself with an interval returned by `autoUpdateInterval`.
    /// This method can be overridden by implementation classes to define a view instance-specific update
    /// interval. The default update interval for all views of an actor system can be configured with the
    /// `akka.persistence.view.auto-update-interval` configuration key. Applications may trigger additional
    /// view updates by sending the view <see cref="Update"/> requests. See also methods
    /// </summary>
    public abstract class PersistentView : Recovery
    {
    }
}