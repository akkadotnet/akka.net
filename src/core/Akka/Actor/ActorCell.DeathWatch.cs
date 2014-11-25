using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Dispatch.SysMsg;
using Akka.Event;

namespace Akka.Actor
{
    partial class ActorCell
    {
        HashSet<ActorRef> _watching = new HashSet<ActorRef>();
        readonly HashSet<ActorRef> _watchedBy = new HashSet<ActorRef>();
        HashSet<ActorRef> _terminatedQueue = new HashSet<ActorRef>();//terminatedqueue should never be used outside the message loop

        public ActorRef Watch(ActorRef subject)
        {
            var a = (InternalActorRef)subject;

            if (!a.Equals(Self) && !WatchingContains(a))
            {
                MaintainAddressTerminatedSubscription(() =>
                {
                    a.Tell(new Watch(a, Self)); // ➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS
                    _watching.Add(a);                        
                }, a);
            }
            return a;
        }

        public ActorRef Unwatch(ActorRef subject)
        {
            var a = (InternalActorRef)subject;
            if (! a.Equals(Self) && WatchingContains(a))
            {
                a.Tell(new Unwatch(a, Self));
                MaintainAddressTerminatedSubscription(() =>
                {
                    _watching = RemoveFromSet(a, _watching);
                }, a);
            }
            _terminatedQueue = RemoveFromSet(a, _terminatedQueue);
            return a;
        }

        protected void ReceivedTerminated(Terminated t)
        {
            if (_terminatedQueue.Contains(t.ActorRef))
            {
                _terminatedQueue.Remove(t.ActorRef); // here we know that it is the SAME ref which was put in
                ReceiveMessage(t);
            }
        }

        /// <summary>
        /// When this actor is watching the subject of [[akka.actor.Terminated]] message
        /// it will be propagated to user's receive.
        /// </summary>
        protected void WatchedActorTerminated(ActorRef actor, bool existenceConfirmed, bool addressTerminated)
        {
            if (WatchingContains(actor))
            {
                MaintainAddressTerminatedSubscription(() =>
                {
                    _watching = RemoveFromSet(actor, _watching);
                }, actor);
                if (!IsTerminating)
                {
                    Self.Tell(new Terminated(actor, existenceConfirmed, addressTerminated), actor);
                    TerminatedQueuedFor(actor);
                }
            }
            if (ChildrenContainer.Contains(actor))
            {
                HandleChildTerminated(actor);
            }
        }

        public void TerminatedQueuedFor(ActorRef subject)
        {
            _terminatedQueue.Add(subject);
        }

        private bool WatchingContains(ActorRef subject)
        {
            return _watching.Contains(subject) ||
                   (subject.Path.Uid != ActorCell.UndefinedUid && _watching.Contains(new UndefinedUidActorRef(subject)));
        }

        private HashSet<ActorRef> RemoveFromSet(ActorRef subject, HashSet<ActorRef> set)
        {
            if (subject.Path.Uid != ActorCell.UndefinedUid)
            {
                set.Remove(subject);
                set.Remove(new UndefinedUidActorRef(subject));
                return set;
            }

            return new HashSet<ActorRef>(set.Where(a => !a.Path.Equals(subject.Path)));
        }

        protected void TellWatchersWeDied()
        {
            if (_watchedBy.Count==0) return;
            try
            {
                // Don't need to send to parent parent since it receives a DWN by default

                /*
                * It is important to notify the remote watchers first, otherwise RemoteDaemon might shut down, causing
                * the remoting to shut down as well. At this point Terminated messages to remote watchers are no longer
                * deliverable.
                *
                * The problematic case is:
                *  1. Terminated is sent to RemoteDaemon
                *   1a. RemoteDaemon is fast enough to notify the terminator actor in RemoteActorRefProvider
                *   1b. The terminator is fast enough to enqueue the shutdown command in the remoting
                *  2. Only at this point is the Terminated (to be sent remotely) enqueued in the mailbox of remoting
                *
                * If the remote watchers are notified first, then the mailbox of the Remoting will guarantee the correct order.
                */
                foreach (var w in _watchedBy) SendTerminated(false, w);
                foreach (var w in _watchedBy) SendTerminated(true, w);
            }
            finally
            {
                _watching = new HashSet<ActorRef>();
            }
        }

        private void SendTerminated(bool ifLocal, ActorRef watcher)
        {
            if (((ActorRefScope)watcher).IsLocal == ifLocal && !watcher.Equals(Parent))
            {
                ((InternalActorRef)watcher).Tell(new DeathWatchNotification(Self, true, false));
            }
        }

        protected void UnwatchWatchedActors(ActorBase actor)
        {
            if(_watching.Count==0) return;
            MaintainAddressTerminatedSubscription(() =>
            {
                try
                {
                    foreach ( // ➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS
                        var watchee in _watching.OfType<InternalActorRef>())
                        watchee.Tell(new Unwatch(watchee, Self));
                }
                finally
                {
                    _watching = new HashSet<ActorRef>();
                    _terminatedQueue = new HashSet<ActorRef>();
                }
            });
        }

        protected void AddWatcher(ActorRef watchee, ActorRef watcher)
        {
            var watcheeSelf = watchee.Equals(Self);
            var watcherSelf = watcher.Equals(Self);

            if (watcheeSelf && !watcherSelf)
            {
                if(!_watchedBy.Contains(watcher)) MaintainAddressTerminatedSubscription(() =>
                {
                    _watchedBy.Add(watcher);
                    if(System.Settings.DebugLifecycle) Publish(new Debug(Self.Path.ToString(), Actor.GetType(), string.Format("now watched by {0}", watcher)));
                }, watcher);
            }
            else if (!watcheeSelf && watcherSelf)
            {
                Watch(watchee);
            }
            else
            {
                Publish(new Warning(Self.Path.ToString(), Actor.GetType(), string.Format("BUG: illegal Watch({0},{1} for {2}", watchee, watcher, Self)));
            }
        }

        protected void RemWatcher(ActorRef watchee, ActorRef watcher)
        {
            var watcheeSelf = watchee.Equals(Self);
            var watcherSelf = watcher.Equals(Self);

            if (watcheeSelf && !watcherSelf)
            {
                if( _watchedBy.Contains(watcher)) MaintainAddressTerminatedSubscription(() =>
                {
                    _watchedBy.Remove(watcher);
                    if (System.Settings.DebugLifecycle) Publish(new Debug(Self.Path.ToString(), Actor.GetType(), string.Format("no longer watched by {0}", watcher)));
                } , watcher);
            }
            else if (!watcheeSelf && watcherSelf)
            {
                Unwatch(watchee);
            }
            else
            {
                Publish(new Warning(Self.Path.ToString(), Actor.GetType(), string.Format("BUG: illegal Unwatch({0},{1} for {2}", watchee, watcher, Self)));
            }
        }

        protected void AddressTerminated(Address address)
        {
            // cleanup watchedBy since we know they are dead
            MaintainAddressTerminatedSubscription(() =>
            {
                foreach (var a in _watchedBy.Where(a => a.Path.Address == address)) _watchedBy.Remove(a);
            });

            // send DeathWatchNotification to self for all matching subjects
            // that are not child with existenceConfirmed = false because we could have been watching a
            // non-local ActorRef that had never resolved before the other node went down
            // When a parent is watching a child and it terminates due to AddressTerminated
            // it is removed by sending DeathWatchNotification with existenceConfirmed = true to support
            // immediate creation of child with same name.
            foreach(var a in _watching.Where(a => a.Path.Address == address))
            {
                Self.Tell(new DeathWatchNotification(a, true /*TODO: childrenRefs.getByRef(a).isDefined*/, true));
            }
        }

        /// <summary>
        /// Starts subscription to AddressTerminated if not already subscribing and the
        /// block adds a non-local ref to watching or watchedBy.
        /// Ends subscription to AddressTerminated if subscribing and the
        /// block removes the last non-local ref from watching and watchedBy.
        /// </summary>
        private void MaintainAddressTerminatedSubscription(Action block, ActorRef change= null)
        {
            if (IsNonLocal(change))
            {
                var had = HasNonLocalAddress();
                block();
                var has = HasNonLocalAddress();
                if (had && !has) UnsubscribeAddressTerminated();
                else if (!had && has) SubscribeAddressTerminated();
            }
            else
            {
                block();
            }
        }

        private static bool IsNonLocal(ActorRef @ref)
        {
            if (@ref == null) return true;
            var a = @ref as InternalActorRef;
            if (a != null && !a.IsLocal) return true;
            return false;
        }

        private bool HasNonLocalAddress()
        {
            return _watching.Any(IsNonLocal) || _watchedBy.Any(IsNonLocal);
        }

        private void UnsubscribeAddressTerminated()
        {
            AddressTerminatedTopic.Get(System).Unsubscribe(Self);
        }

        private void SubscribeAddressTerminated()
        {
            AddressTerminatedTopic.Get(System).Subscribe(Self);
        }
    }

    class UndefinedUidActorRef : MinimalActorRef
    {
        readonly ActorRef _ref;

        public UndefinedUidActorRef(ActorRef @ref)
        {
            _ref = @ref;
        }

        public override ActorPath Path
        {
            get { return _ref.Path.WithUid(ActorCell.UndefinedUid); }
        }

        public override ActorRefProvider Provider
        {
            get
            {
                throw new NotImplementedException("UndefinedUidActorRef does not provide");
            }
        }
    }
    

}
