//-----------------------------------------------------------------------
// <copyright file="IRunnable.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Dispatch
{
    /// <summary>
    /// An asynchronous operation will be executed by a <see cref="MessageDispatcher"/>.
    /// </summary>
    public interface IRunnable
    {
        /// <summary>
        /// TBD
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
        /// TBD
        /// </summary>
        /// <param name="action">TBD</param>
        public ActionRunnable(Action action)
        {
            _action = action;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void Run()
        {
            _action();
        }
    }

    /// <summary>
    /// <see cref="IRunnable"/> which executes an <see cref="Action{state}"/> and an <see cref="object"/> representing the state.
    /// </summary>
    public sealed class ActionWithStateRunnable : IRunnable
    {
        private readonly Action<object> _actionWithState;
        private readonly object _state;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actionWithState">TBD</param>
        /// <param name="state">TBD</param>
        public ActionWithStateRunnable(Action<object> actionWithState, object state)
        {
            _actionWithState = actionWithState;
            _state = state;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void Run()
        {
            _actionWithState(_state);
        }
    }
}

