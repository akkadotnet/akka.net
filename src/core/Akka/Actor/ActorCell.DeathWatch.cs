//-----------------------------------------------------------------------
// <copyright file="ActorCell.DeathWatch.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Actor
{
    partial class ActorCell
    {
        private IActorState _state = new DefaultActorState();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subject">TBD</param>
        /// <returns>TBD</returns>
        public IActorRef Watch(IActorRef subject)
        {
            var a = (IInternalActorRef)subject;

            if (!a.Equals(Self) && !WatchingContains(a))
            {
                MaintainAddressTerminatedSubscription(() =>
                {
                    a.SendSystemMessage(new Watch(a, _self)); // ➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS
                    _state = _state.AddWatching(a, Option<object>.None);
                }, a);
            }
            return a;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subject">TBD</param>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        public IActorRef WatchWith(IActorRef subject, object message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message), "message must not be null");

            var a = (IInternalActorRef)subject;

            if (!a.Equals(Self) && !WatchingContains(a))
            {
                MaintainAddressTerminatedSubscription(() =>
                {
                    a.SendSystemMessage(new Watch(a, _self)); // ➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS
                    _state = _state.AddWatching(a, message);
                }, a);
            }
            return a;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subject">TBD</param>
        /// <returns>TBD</returns>
        public IActorRef Unwatch(IActorRef subject)
        {
            var a = (IInternalActorRef)subject;
            if (!a.Equals(Self) && WatchingContains(a))
            {
                a.SendSystemMessage(new Unwatch(a, _self));
                MaintainAddressTerminatedSubscription(() =>
                {
                    _state = _state.RemoveWatching(a);
                }, a);
            }
            (_state, _) = _state.RemoveTerminated(a);
            return a;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="t">TBD</param>
        protected void ReceivedTerminated(Terminated t)
        {
            if (!_state.ContainsTerminated(t.ActorRef))
                return;

            Option<object> customTerminatedMessage;
            (_state, customTerminatedMessage) = _state.RemoveTerminated(t.ActorRef); // here we know that it is the SAME ref which was put in
            ReceiveMessage(customTerminatedMessage.GetOrElse(t));
        }

        /// <summary>
        /// When this actor is watching the subject of <see cref="Terminated"/> message
        /// it will be propagated to user's receive.
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <param name="existenceConfirmed">TBD</param>
        /// <param name="addressTerminated">TBD</param>
        protected void WatchedActorTerminated(IActorRef actor, bool existenceConfirmed, bool addressTerminated)
        {
            if (TryGetWatching(actor, out var message)) // message is custom termination message that was requested
            {
                MaintainAddressTerminatedSubscription(() =>
                {
                    _state = _state.RemoveWatching(actor);
                }, actor);
                if (!IsTerminating)
                {
                    // Unwatch could be called somewhere there inbetween here and the actual delivery of the custom message
                    Self.Tell(new Terminated(actor, existenceConfirmed, addressTerminated), actor);
                    TerminatedQueuedFor(actor, message);
                }
            }
            if (ChildrenContainer.Contains(actor))
            {
                HandleChildTerminated(actor);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subject">Tracked subject</param>
        /// <param name="customMessage">Terminated custom message</param>
        public void TerminatedQueuedFor(IActorRef subject, Option<object> customMessage)
        {
            _state = _state.AddTerminated(subject, customMessage);
        }

        private bool WatchingContains(IActorRef subject)
        {
            return _state.ContainsWatching(subject);
        }

        private bool TryGetWatching(IActorRef subject, out Option<object> message)
        {
            return _state.TryGetWatching(subject, out message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected void TellWatchersWeDied()
        {
            var watchedBy = _state
                .GetWatchedBy()
                .ToList();

            if (watchedBy.Count == 0) return;
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
                foreach (var w in watchedBy) SendTerminated(false, (IInternalActorRef)w);
                foreach (var w in watchedBy) SendTerminated(true, (IInternalActorRef)w);
            }
            finally
            {
                MaintainAddressTerminatedSubscription(() =>
                {
                    _state = _state.ClearWatchedBy();
                });
            }
        }

        private void SendTerminated(bool ifLocal, IInternalActorRef watcher)
        {
            if (((IActorRefScope)watcher).IsLocal == ifLocal && !watcher.Equals(Parent))
            {
                watcher.SendSystemMessage(new DeathWatchNotification(Self, true, false));
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
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
                        watchee.SendSystemMessage(new Unwatch(watchee, _self));
                }
                finally
                {
                    _state = _state.ClearWatching();
                    _state = _state.ClearTerminated();
                }
            });
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="watchee">TBD</param>
        /// <param name="watcher">TBD</param>
        protected void AddWatcher(IActorRef watchee, IActorRef watcher)
        {
            var watcheeSelf = watchee.Equals(Self);
            var watcherSelf = watcher.Equals(Self);

            if (watcheeSelf && !watcherSelf)
            {
                if (!_state.ContainsWatchedBy(watcher)) MaintainAddressTerminatedSubscription(() =>
                {
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="watchee">TBD</param>
        /// <param name="watcher">TBD</param>
        protected void RemWatcher(IActorRef watchee, IActorRef watcher)
        {
            var watcheeSelf = watchee.Equals(Self);
            var watcherSelf = watcher.Equals(Self);

            if (watcheeSelf && !watcherSelf)
            {
                if (_state.ContainsWatchedBy(watcher)) MaintainAddressTerminatedSubscription(() =>
                {
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="address">TBD</param>
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
                ((IInternalActorRef)Self).SendSystemMessage(new DeathWatchNotification(a, true /*TODO: childrenRefs.getByRef(a).isDefined*/, true));
            }
        }

        /// <summary>
        /// Starts subscription to AddressTerminated if not already subscribing and the
        /// block adds a non-local ref to watching or watchedBy.
        /// Ends subscription to AddressTerminated if subscribing and the
        /// block removes the last non-local ref from watching and watchedBy.
        /// </summary>
        /// <param name="block">TBD</param>
        /// <param name="change">TBD</param>
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
}
