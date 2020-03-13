//-----------------------------------------------------------------------
// <copyright file="ActorState.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Util;

namespace Akka.Actor
{
    /// <summary>
    /// This interface represents the parts of the internal actor state; the behavior stack, watched by, watching and termination queue
    /// </summary>
    internal interface IActorState 
    {
        /// <summary>
        /// Removes the provided <see cref="IActorRef"/> from the `Watching` set
        /// </summary>
        /// <param name="actor">The <see cref="IActorRef"/> to be removed</param>
        /// <returns>TBD</returns>
        IActorState RemoveWatching(IActorRef actor);
        /// <summary>
        /// Removes the provided <see cref="IActorRef"/> from the `WatchedBy` set
        /// </summary>
        /// <param name="actor">The <see cref="IActorRef"/> to be removed</param>
        /// <returns>TBD</returns>
        IActorState RemoveWatchedBy(IActorRef actor);
        /// <summary>
        /// Removes the provided <see cref="IActorRef"/> from the `Termination queue` set
        /// </summary>
        /// <param name="actor">The <see cref="IActorRef"/> to be removed</param>
        /// <returns>Updated state and custom terminated message, if any</returns>
        (IActorState State, Option<object> CustomMessage) RemoveTerminated(IActorRef actor);
        /// <summary>
        /// Adds the provided <see cref="IActorRef"/> to the `Watching` set
        /// </summary>
        /// <param name="actor">The <see cref="IActorRef"/> to be added</param>
        /// <param name="message">The message sent on termination, None for default <see cref="Terminated"/> message</param>
        /// <returns>TBD</returns>
        IActorState AddWatching(IActorRef actor, Option<object> message);
        /// <summary>
        /// Adds the provided <see cref="IActorRef"/> to the `WatchedBy` set
        /// </summary>
        /// <param name="actor">The <see cref="IActorRef"/> to be added</param>
        /// <returns>TBD</returns>
        IActorState AddWatchedBy(IActorRef actor);

        /// <summary>
        /// Adds the provided <see cref="IActorRef"/> to the `Termination queue` set, with optional custom terminated message
        /// </summary>
        /// <param name="actor">The <see cref="IActorRef"/> to be added</param>
        /// <param name="customMessage">Optional custom terminated message</param>
        /// <returns>Updated state</returns>
        IActorState AddTerminated(IActorRef actor, Option<object> customMessage);
        /// <summary>
        /// Clears the `Watching` set
        /// </summary>
        /// <returns>TBD</returns>
        IActorState ClearWatching();
        /// <summary>
        /// Clears the `WatchedBy` set
        /// </summary>
        /// <returns>TBD</returns>
        IActorState ClearWatchedBy();
        /// <summary>
        /// Clears the `Termination queue` set
        /// </summary>
        /// <returns>TBD</returns>
        IActorState ClearTerminated();
        /// <summary>
        /// Clears the `Behavior` stack
        /// </summary>
        /// <returns>TBD</returns>
        IActorState ClearBehaviorStack();
        /// <summary>
        /// Replaces the current receive behavior with a new behavior
        /// </summary>
        /// <param name="receive">The new behavior</param>
        /// <returns>TBD</returns>
        IActorState Become(Receive receive);
        /// <summary>
        /// Pushes a new receive behavior onto the `Behavior` stack
        /// </summary>
        /// <param name="receive">The new top level behavior</param>
        /// <returns>TBD</returns>
        IActorState BecomeStacked(Receive receive);
        /// <summary>
        /// Removes the top level receive behavior from the `Behavior` stack
        /// </summary>
        /// <returns>TBD</returns>
        IActorState UnbecomeStacked();
        /// <summary>
        /// Determines whether the provided <see cref="IActorRef"/> is present in the `Watching` set
        /// </summary>
        /// <param name="actor">The <see cref="IActorRef"/> to locate in the `Watching` set</param>
        /// <returns>TBD</returns>
        bool ContainsWatching(IActorRef actor);
        /// <summary>
        /// Determines whether the provided <see cref="IActorRef"/> is present in the `Watching` set 
        /// and retrieves the potential custom termination message
        /// </summary>
        /// <param name="actor">The <see cref="IActorRef"/> to locate in the `Watching` set</param>
        /// <param name="msg">
        /// The custom termination message or None if the default <see cref="Terminated"/> message is expected
        /// </param>
        /// <returns></returns>
        bool TryGetWatching(IActorRef actor, out Option<object> msg);
        /// <summary>
        /// Determines whether the provided <see cref="IActorRef"/> is present in the `WatchedBy` set
        /// </summary>
        /// <param name="actor">The <see cref="IActorRef"/> to locate in the `WatchedBy` set</param>
        /// <returns>TBD</returns>
        bool ContainsWatchedBy(IActorRef actor);
        /// <summary>
        /// Determines whether the provided <see cref="IActorRef"/> is present in the `Termination queue` set
        /// </summary>
        /// <param name="actor">The <see cref="IActorRef"/> to locate in the `Termination queue` set</param>
        /// <returns>TBD</returns>
        bool ContainsTerminated(IActorRef actor);
        /// <summary>
        /// Returns an <see cref="IEnumerable{IActorRef}"/> over the `Watching` set
        /// </summary>
        /// <returns>TBD</returns>
        IEnumerable<IActorRef> GetWatching();
        /// <summary>
        /// Returns an <see cref="IEnumerable{IActorRef}"/> over the `WatchedBy` set
        /// </summary>
        /// <returns>TBD</returns>
        IEnumerable<IActorRef> GetWatchedBy();
        /// <summary>
        /// Returns an <see cref="IEnumerable{IActorRef}"/> over the `Termination queue` set
        /// </summary>
        /// <returns>TBD</returns>
        IEnumerable<IActorRef> GetTerminated();
        /// <summary>
        /// Returns the top level receive behavior from the behavior stack
        /// </summary>
        /// <returns>TBD</returns>
        Receive GetCurrentBehavior();
    }

    /// <summary>
    /// Represents the default start up state for any actor.
    /// This state provides capacity for one `WatchedBy` and one `Receive` behavior
    /// As soon as this container is no longer enough to contain the current state
    /// The state container will escalate into a `FullActorState` instance
    /// </summary>
    internal class DefaultActorState : IActorState
    {
        private IActorRef _watchedBy;
        private Receive _receive;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public IActorState RemoveWatching(IActorRef actor)
        {
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public IActorState RemoveWatchedBy(IActorRef actor)
        {
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public (IActorState, Option<object>) RemoveTerminated(IActorRef actor)
        {
            return (this, Option<object>.None);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <param name="msg">TBD</param>
        /// <returns>TBD</returns>
        public IActorState AddWatching(IActorRef actor, Option<object> msg)
        {
            return GetFullState().AddWatching(actor, msg);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public IActorState AddWatchedBy(IActorRef actor)
        {
            //if we have no watchedBy, assign it to our local ref
            //this is a memory footprint optimization, we can have a DefaultActorState object that is watched by _one_ watcher (parent)
            //in every other case, we escalate to FullActorState
            if (_watchedBy == null)
            {
                _watchedBy = actor;
                return this;
            }
            //otherwise, add our existing watchedBy and the new actor to the new fullstate container
            return GetFullState().AddWatchedBy(actor);

        }

        private FullActorState GetFullState()
        {
            var res = new FullActorState();
            if (_receive != null)
                res.Become(_receive);

            if (_watchedBy != null)
                res.AddWatchedBy(_watchedBy);

            return res;
        }

        /// <summary>
        /// Adds terminated actor with optional custom terminated message
        /// </summary>
        /// <param name="actor">Terminated actor</param>
        /// <param name="customMessage">Custom terminated message</param>
        /// <returns>Updated state</returns>
        public IActorState AddTerminated(IActorRef actor, Option<object> customMessage)
        {
            return GetFullState().AddTerminated(actor, customMessage);
        }
        
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public bool ContainsWatching(IActorRef actor)
        {
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <param name="msg">TBD</param>
        /// <returns>TBD</returns>
        public bool TryGetWatching(IActorRef actor, out Option<object> msg)
        {
            return GetFullState().TryGetWatching(actor, out msg);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public bool ContainsWatchedBy(IActorRef actor)
        {
            if (_watchedBy == null)
                return false;

            return _watchedBy.Equals(actor);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public bool ContainsTerminated(IActorRef actor)
        {
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IEnumerable<IActorRef> GetWatching()
        {
            yield break;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IEnumerable<IActorRef> GetWatchedBy()
        {
            if (_watchedBy != null)
                yield return _watchedBy;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IEnumerable<IActorRef> GetTerminated()
        {
            yield break;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IActorState ClearWatching()
        {
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IActorState ClearWatchedBy()
        {
            _watchedBy = null;
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IActorState ClearTerminated()
        {
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="receive">TBD</param>
        /// <returns>TBD</returns>
        public IActorState Become(Receive receive)
        {
            if (_receive == null)
            {
                _receive = receive;
                return this;
            }
            return GetFullState().BecomeStacked(receive);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="receive">TBD</param>
        /// <returns>TBD</returns>
        public IActorState BecomeStacked(Receive receive)
        {
            if (_receive == null)
            {
                _receive = receive;
                return this;
            }
            return GetFullState().BecomeStacked(receive);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IActorState UnbecomeStacked()
        {
            return this;
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IActorState ClearBehaviorStack()
        {
            _receive = null;
            return this;
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public Receive GetCurrentBehavior()
        {
            //TODO: throw if null?
            return _receive;
        }
    }

    /// <summary>
    /// Represents the full state of an actor, this is used whenever an actor need more state than the `DefaultActorState` container can contain
    /// </summary>
    internal class FullActorState : IActorState
    {
        private readonly Dictionary<IActorRef, Option<object>> _watching = new Dictionary<IActorRef, Option<object>>();
        private readonly HashSet<IActorRef> _watchedBy = new HashSet<IActorRef>();
        private readonly Dictionary<IActorRef, Option<object>> _terminatedQueue = new Dictionary<IActorRef, Option<object>>(); //terminatedqueue should never be used outside the message loop
        private Stack<Receive> _behaviorStack = new Stack<Receive>(2);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public IActorState RemoveWatching(IActorRef actor)
        {
            _watching.Remove(actor);
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public IActorState RemoveWatchedBy(IActorRef actor)
        {
            _watchedBy.Remove(actor);
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public (IActorState, Option<object>) RemoveTerminated(IActorRef actor)
        {
            if (!_terminatedQueue.ContainsKey(actor)) 
                return (this, Option<object>.None);
            
            var customMessage = _terminatedQueue[actor];
            _terminatedQueue.Remove(actor);
            return (this, customMessage);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <param name="msg">TBD</param>
        /// <returns>TBD</returns>
        public IActorState AddWatching(IActorRef actor, Option<object> msg)
        {
            _watching.Add(actor, msg);
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public IActorState AddWatchedBy(IActorRef actor)
        {
            _watchedBy.Add(actor);
            return this;
        }

        /// <summary>
        /// Adds terminated actor with optional custom terminated message
        /// </summary>
        /// <param name="actor">Terminated actor</param>
        /// <param name="customMessage">Custom terminated message</param>
        /// <returns>Updated state</returns>
        public IActorState AddTerminated(IActorRef actor, Option<object> customMessage)
        {
            if (!_terminatedQueue.ContainsKey(actor))
                _terminatedQueue.Add(actor, customMessage);
            
            return this;
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public bool ContainsWatching(IActorRef actor)
        {
            return _watching.ContainsKey(actor);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <param name="msg">TBD</param>
        /// <returns>TBD</returns>
        public bool TryGetWatching(IActorRef actor, out Option<object> msg)
        {
            return _watching.TryGetValue(actor, out msg);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public bool ContainsWatchedBy(IActorRef actor)
        {
            return _watchedBy.Contains(actor);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public bool ContainsTerminated(IActorRef actor)
        {
            return _terminatedQueue.ContainsKey(actor);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IEnumerable<IActorRef> GetWatching()
        {
            return _watching.Keys;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IEnumerable<IActorRef> GetWatchedBy()
        {
            return _watchedBy;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IEnumerable<IActorRef> GetTerminated()
        {
            return _terminatedQueue.Keys;
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IActorState ClearWatching()
        {
            _watching.Clear();
            return this;
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IActorState ClearWatchedBy()
        {
            _watchedBy.Clear();
            return this;
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IActorState ClearTerminated()
        {
            _terminatedQueue.Clear();
            return this;
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="receive">TBD</param>
        /// <returns>TBD</returns>
        public IActorState Become(Receive receive)
        {
            if (_behaviorStack.Count > 1) //We should never pop off the initial receiver
                _behaviorStack.Pop();
            _behaviorStack.Push(receive);
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="receive">TBD</param>
        /// <returns>TBD</returns>
        public IActorState BecomeStacked(Receive receive)
        {
            _behaviorStack.Push(receive);
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IActorState UnbecomeStacked()
        {
            if (_behaviorStack.Count > 1) //We should never pop off the initial receiver
                _behaviorStack.Pop();

            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IActorState ClearBehaviorStack()
        {
            _behaviorStack = new Stack<Receive>(1);
            return this;
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public Receive GetCurrentBehavior()
        {
            return _behaviorStack.Peek();
        }
    }
}
