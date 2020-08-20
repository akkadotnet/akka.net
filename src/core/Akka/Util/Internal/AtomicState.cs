//-----------------------------------------------------------------------
// <copyright file="AtomicState.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace Akka.Util.Internal
{
    /// <summary>
    /// Internal state abstraction
    /// </summary>
    internal abstract class AtomicState : AtomicCounterLong, IAtomicState
    {
        private readonly ConcurrentQueue<Action> _listeners;
        private readonly TimeSpan _callTimeout;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="callTimeout">TBD</param>
        /// <param name="startingCount">TBD</param>
        protected AtomicState(TimeSpan callTimeout, long startingCount)
            : base(startingCount)
        {
            _listeners = new ConcurrentQueue<Action>();
            _callTimeout = callTimeout;
        }

        /// <summary>
        /// Add a listener function which is invoked on state entry
        /// </summary>
        /// <param name="listener">listener implementation</param>
        public void AddListener(Action listener)
        {
            _listeners.Enqueue(listener);
        }

        /// <summary>
        /// Test for whether listeners exist
        /// </summary>
        public bool HasListeners
        {
            get { return !_listeners.IsEmpty; }
        }

        /// <summary>
        /// Notifies the listeners of the transition event via a 
        /// </summary>
        /// <returns>TBD</returns>
        protected async Task NotifyTransitionListeners()
        {
            if (!HasListeners) return;
            await Task
                .Factory
                .StartNew
                (
                    () =>
                    {
                        foreach (var listener in _listeners)
                        {
                            listener.Invoke();
                        }
                    }
                ).ConfigureAwait(false);
        }

        /// <summary>
        /// Shared implementation of call across all states.  Thrown exception or execution of the call beyond the allowed
        /// call timeout is counted as a failed call, otherwise a successful call
        /// 
        /// NOTE: In .Net there is no way to cancel an uncancellable task. We are merely cancelling the wait and marking this
        /// as a failure.
        /// 
        /// see http://blogs.msdn.com/b/pfxteam/archive/2011/11/10/10235834.aspx 
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="task">Implementation of the call</param>
        /// <returns>result of the call</returns>
        public async Task<T> CallThrough<T>(Func<Task<T>> task)
        {
            var deadline = DateTime.UtcNow.Add(_callTimeout);
            ExceptionDispatchInfo capturedException = null;
            T result = default(T);
            try
            {
                result = await task().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                capturedException = ExceptionDispatchInfo.Capture(ex);
            }

            // Need to make sure that timeouts are reported as timeouts
            if (capturedException != null)
            {
                CallFails(capturedException.SourceException);
                capturedException.Throw();
            }
            else if (DateTime.UtcNow.CompareTo(deadline) >= 0)
            {
                CallFails(new TimeoutException(
                    $"Execution did not complete within the time allotted {_callTimeout.TotalMilliseconds} ms"));
            }
            else
            {
                CallSucceeds();
            }
            return result;
        }

        /// <summary>
        /// Shared implementation of call across all states.  Thrown exception or execution of the call beyond the allowed
        /// call timeout is counted as a failed call, otherwise a successful call
        /// 
        /// NOTE: In .Net there is no way to cancel an uncancellable task. We are merely cancelling the wait and marking this
        /// as a failure.
        /// 
        /// see http://blogs.msdn.com/b/pfxteam/archive/2011/11/10/10235834.aspx 
        /// </summary>
        /// <param name="task"><see cref="Task"/> Implementation of the call</param>
        /// <returns><see cref="Task"/></returns>
        public async Task CallThrough(Func<Task> task)
        {
            var deadline = DateTime.UtcNow.Add(_callTimeout);
            ExceptionDispatchInfo capturedException = null;

            try
            {
                await task().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                capturedException = ExceptionDispatchInfo.Capture(ex);
            }

            // Need to make sure that timeouts are reported as timeouts
            if (capturedException != null)
            {
                CallFails(capturedException?.SourceException);
                capturedException.Throw();
            } 
            else if (DateTime.UtcNow.CompareTo(deadline) >= 0)
            {
                CallFails(new TimeoutException(
                    $"Execution did not complete within the time allotted {_callTimeout.TotalMilliseconds} ms"));
            }
            else
            {
                CallSucceeds();
            }


        }

        /// <summary>
        /// Abstract entry point for all states
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="body">Implementation of the call that needs protected</param>
        /// <returns><see cref="Task"/> containing result of protected call</returns>
        public abstract Task<T> Invoke<T>(Func<Task<T>> body);

        /// <summary>
        /// Abstract entry point for all states
        /// </summary>
        /// <param name="body">Implementation of the call that needs protected</param>
        /// <returns><see cref="Task"/> containing result of protected call</returns>
        public abstract Task Invoke(Func<Task> body);

        /// <summary>
        /// Invoked when call fails
        /// </summary>
        protected internal abstract void CallFails(Exception cause);

        /// <summary>
        /// Invoked when call succeeds
        /// </summary>
        protected internal abstract void CallSucceeds();

        /// <summary>
        /// Invoked on the transitioned-to state during transition. Notifies listeners after invoking subclass template method _enter
        /// </summary>
        protected abstract void EnterInternal();

        /// <summary>
        /// Enter the state. NotifyTransitionListeners is not awaited -- its "fire and forget". 
        /// It is up to the user to handle any errors that occur in this state.
        /// </summary>
        public void Enter()
        {
            EnterInternal();
            NotifyTransitionListeners();
        }

    }

    /// <summary>
    /// This interface represents the parts of the internal circuit breaker state; the behavior stack, watched by, watching and termination queue
    /// </summary>
    public interface IAtomicState
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="listener">TBD</param>
        void AddListener(Action listener);
        /// <summary>
        /// TBD
        /// </summary>
        bool HasListeners { get; }
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="body">TBD</param>
        /// <returns>TBD</returns>
        Task<T> Invoke<T>(Func<Task<T>> body);
        /// <summary>
        /// TBD
        /// </summary>
        void Enter();
    }
}
