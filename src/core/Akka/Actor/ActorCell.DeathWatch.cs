using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Dispatch.SysMsg;
using Akka.Event;

namespace Akka.Actor
{
    partial class ActorCell
    {
        HashSet<IActorRef> _watching = new HashSet<IActorRef>();
        readonly HashSet<IActorRef> _watchedBy = new HashSet<IActorRef>();
        HashSet<IActorRef> _terminatedQueue = new HashSet<IActorRef>();//terminatedqueue should never be used outside the message loop

        public IActorRef Watch(IActorRef subject)
        {
            var a = (IInternalActorRef)subject;

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

        public IActorRef Unwatch(IActorRef subject)
        {
            var a = (IInternalActorRef)subject;
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
        /// When this actor is watching the subject of <see cref="Terminated"/> message
        /// it will be propagated to user's receive.
        /// </summary>
        protected void WatchedActorTerminated(IActorRef actor, bool existenceConfirmed, bool addressTerminated)
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

        public void TerminatedQueuedFor(IActorRef subject)
        {
            _terminatedQueue.Add(subject);
        }

        private bool WatchingContains(IActorRef subject)
        {
            return _watching.Contains(subject) ||
                   (subject.Path.Uid != ActorCell.UndefinedUid && _watching.Contains(new UndefinedUidActorRef(subject)));
        }

        private HashSet<IActorRef> RemoveFromSet(IActorRef subject, HashSet<IActorRef> set)
        {
            if (subject.Path.Uid != ActorCell.UndefinedUid)
            {
                set.Remove(subject);
                set.Remove(new UndefinedUidActorRef(subject));
                return set;
            }

            return new HashSet<IActorRef>(set.Where(a => !a.Path.Equals(subject.Path)));
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
                _watching = new HashSet<IActorRef>();
            }
        }

        private void SendTerminated(bool ifLocal, IActorRef watcher)
        {
            if (((ActorRefScope)watcher).IsLocal == ifLocal && !watcher.Equals(Parent))
            {
                ((IInternalActorRef)watcher).Tell(new DeathWatchNotification(Self, true, false));
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
                        var watchee in _watching.OfType<IInternalActorRef>())
                        watchee.Tell(new Unwatch(watchee, Self));
                }
                finally
                {
                    _watching = new HashSet<IActorRef>();
                    _terminatedQueue = new HashSet<IActorRef>();
                }
            });
        }

        protected void AddWatcher(IActorRef watchee, IActorRef watcher)
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

        protected void RemWatcher(IActorRef watchee, IActorRef watcher)
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
        private void MaintainAddressTerminatedSubscription(Action block, IActorRef change= null)
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

        private static bool IsNonLocal(IActorRef @ref)
        {
            if (@ref == null) return true;
            var a = @ref as IInternalActorRef;
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
        readonly IActorRef _ref;

        public UndefinedUidActorRef(IActorRef @ref)
        {
            _ref = @ref;
        }

        public override ActorPath Path
        {
            get { return _ref.Path.WithUid(ActorCell.UndefinedUid); }
        }

        public override IActorRefProvider Provider
        {
            get
            {
                throw new NotImplementedException("UndefinedUidActorRef does not provide");
            }
        }
    }
    

}
