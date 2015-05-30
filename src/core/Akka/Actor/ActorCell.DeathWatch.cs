//-----------------------------------------------------------------------
// <copyright file="ActorCell.DeathWatch.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Dispatch.SysMsg;
using Akka.Event;

namespace Akka.Actor
{
    partial class ActorCell
    {
        private IActorState _state = new DefaultActorState();

        public IActorRef Watch(IActorRef subject)
        {
            var a = (IInternalActorRef)subject;

            if (!a.Equals(Self) && !WatchingContains(a))
            {
                MaintainAddressTerminatedSubscription(() =>
                {
                    a.Tell(new Watch(a, Self)); // ➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS
                    _state = _state.AddWatching(a);
                }, a);
            }
            return a;
        }

        public IActorRef Unwatch(IActorRef subject)
        {
            var a = (IInternalActorRef)subject;
            if (!a.Equals(Self) && WatchingContains(a))
            {
                a.Tell(new Unwatch(a, Self));
                MaintainAddressTerminatedSubscription(() =>
                {
                    _state = _state.RemoveWatching(a);
                }, a);
            }
            _state = _state.RemoveTerminated(a);
            return a;
        }

        protected void ReceivedTerminated(Terminated t)
        {
            if (!_state.ContainsTerminated(t.ActorRef))
                return;

            _state = _state.RemoveTerminated(t.ActorRef); // here we know that it is the SAME ref which was put in
            ReceiveMessage(t);
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
                    _state = _state.RemoveWatching(actor);
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
            _state = _state.AddTerminated(subject);
        }

        private bool WatchingContains(IActorRef subject)
        {
            return _state.ContainsWatching(subject) ||
                   (subject.Path.Uid != UndefinedUid && _state.ContainsWatching(new UndefinedUidActorRef(subject)));
        }

        protected void TellWatchersWeDied()
        {
            var watchedBy = _state
                .GetWatchedBy()
                .ToList();

            if (!watchedBy.Any()) return;
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
                foreach (var w in watchedBy) SendTerminated(false, w);
                foreach (var w in watchedBy) SendTerminated(true, w);
            }
            finally
            {
                _state = _state.ClearWatching();
            }
        }

        private void SendTerminated(bool ifLocal, IActorRef watcher)
        {
            if (((IActorRefScope)watcher).IsLocal == ifLocal && !watcher.Equals(Parent))
            {
                watcher.Tell(new DeathWatchNotification(Self, true, false));
            }
        }

        protected void UnwatchWatchedActors(ActorBase actor)
        {
            var watching = _state
                .GetWatching()
                .ToList();

            if (!watching.Any()) return;

            MaintainAddressTerminatedSubscription(() =>
            {
                try
                {
                    // ➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS
                    foreach (var watchee in watching.OfType<IInternalActorRef>())
                        watchee.Tell(new Unwatch(watchee, Self));
                }
                finally
                {
                    _state = _state.ClearWatching();
                    _state = _state.ClearTerminated();
                }
            });
        }

        protected void AddWatcher(IActorRef watchee, IActorRef watcher)
        {
            var watcheeSelf = watchee.Equals(Self);
            var watcherSelf = watcher.Equals(Self);

            if (watcheeSelf && !watcherSelf)
            {
                if (!_state.ContainsWatchedBy(watcher)) MaintainAddressTerminatedSubscription(() =>
                {
                    //_watchedBy.Add(watcher);
                    _state = _state.AddWatchedBy(watcher);

                    if (System.Settings.DebugLifecycle) Publish(new Debug(Self.Path.ToString(), Actor.GetType(), string.Format("now watched by {0}", watcher)));
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
                if (_state.ContainsWatchedBy(watcher)) MaintainAddressTerminatedSubscription(() =>
                {
                    //_watchedBy.Remove(watcher);
                    _state = _state.RemoveWatchedBy(watcher);

                    if (System.Settings.DebugLifecycle) Publish(new Debug(Self.Path.ToString(), Actor.GetType(), string.Format("no longer watched by {0}", watcher)));
                }, watcher);
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
                
                foreach (var a in _state.GetWatchedBy().Where(a => a.Path.Address == address).ToList())
                {
                    //_watchedBy.Remove(a);
                    _state = _state.RemoveWatchedBy(a);
                }
            });

            // send DeathWatchNotification to self for all matching subjects
            // that are not child with existenceConfirmed = false because we could have been watching a
            // non-local ActorRef that had never resolved before the other node went down
            // When a parent is watching a child and it terminates due to AddressTerminated
            // it is removed by sending DeathWatchNotification with existenceConfirmed = true to support
            // immediate creation of child with same name.
            foreach (var a in _state.GetWatching().Where(a => a.Path.Address == address))
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
        private void MaintainAddressTerminatedSubscription(Action block, IActorRef change = null)
        {
            if (IsNonLocal(change))
            {
                var had = HasNonLocalAddress();
                block();
                var has = HasNonLocalAddress();

                if (had && !has)
                    UnsubscribeAddressTerminated();
                else if (!had && has)
                    SubscribeAddressTerminated();
            }
            else
            {
                block();
            }
        }

        private static bool IsNonLocal(IActorRef @ref)
        {
            if (@ref == null)
                return true;

            var a = @ref as IInternalActorRef;
            return a != null && !a.IsLocal;
        }

        private bool HasNonLocalAddress()
        {
            var watching = _state.GetWatching();
            var watchedBy = _state.GetWatchedBy();
            return watching.Any(IsNonLocal) || watchedBy.Any(IsNonLocal);
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