//-----------------------------------------------------------------------
// <copyright file="IRunnable.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;

namespace Akka.Dispatch
{
    /// <summary>
    /// An asynchronous operation will be executed by a <see cref="MessageDispatcher"/>.
    /// </summary>
#if NETSTANDARD
    public interface IRunnable
#else
    public interface IRunnable : IThreadPoolWorkItem
#endif
    {
        /// <summary>
        /// Executes the task.
        /// </summary>
        void Run();
    }

    /// <summary>
    /// <see cref="IRunnable"/> which executes an <see cref="Action"/>
    /// </summary>
    public sealed class ActionRunnable : IRunnable
    {
        private readonly Action _action;

        /// <summary>
        /// Creates a new thread pool work item that executes a delegate.
        /// </summary>
        /// <param name="action">The delegate to execute</param>
        public ActionRunnable(Action action)
        {
            _action = action;
        }

        public void Run()
        {
            _action();
        }

#if !NETSTANDARD
        public void Execute()
        {
            _action();
        }
#endif
    }

    /// <summary>
    /// <see cref="IRunnable"/> which executes an <see cref="Action{state}"/> and an <see cref="object"/> representing the state.
    /// </summary>
    public sealed class ActionWithStateRunnable : IRunnable
    {
        private readonly Action<object> _actionWithState;
        private readonly object _state;

        /// <summary>
        /// Creates a new thread pool work item that executes a delegate along with state.
        /// </summary>
        /// <param name="actionWithState">The delegate to execute.</param>
        /// <param name="state">The state to execute with this delegate.</param>
        public ActionWithStateRunnable(Action<object> actionWithState, object state)
        {
            _actionWithState = actionWithState;
            _state = state;
        }

        public void Run()
        {
            _actionWithState(_state);
        }

#if !NETSTANDARD
        public void Execute()
        {
            _actionWithState(_state);
        }
#endif
    }
}