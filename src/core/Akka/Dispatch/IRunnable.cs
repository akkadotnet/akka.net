//-----------------------------------------------------------------------
// <copyright file="IRunnable.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Dispatch
{
    /// <summary>
    /// An asynchronous opreation will be executed by a <see cref="MessageDispatcher"/>.
    /// </summary>
    public interface IRunnable
    {
        void Run();
    }

    /// <summary>
    /// <see cref="IRunnable"/> which executes an <see cref="Action"/>
    /// </summary>
    public sealed class ActionRunnable : IRunnable
    {
        private readonly Action _action;

        public ActionRunnable(Action action)
        {
            _action = action;
        }

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

        public ActionWithStateRunnable(Action<object> actionWithState, object state)
        {
            _actionWithState = actionWithState;
            _state = state;
        }

        public void Run()
        {
            _actionWithState(_state);
        }
    }
}

