//-----------------------------------------------------------------------
// <copyright file="Eventsourced.Messages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Persistence
{
    internal interface IPendingHandlerInvocation
    {
        object Event { get; }

        Action<object> Handler { get; }
    }

    /// <summary>
    /// Forces actor to stash incoming commands until all invocations are handled.
    /// </summary>
    internal sealed class StashingHandlerInvocation : IPendingHandlerInvocation
    {
        public StashingHandlerInvocation(object evt, Action<object> handler)
        {
            Event = evt;
            Handler = handler;
        }

        public object Event { get; }

        public Action<object> Handler { get; }
    }

    /// <summary>
    /// Unlike <see cref="StashingHandlerInvocation"/> this one does not force actor to stash commands.
    /// Originates from <see cref="Eventsourced.PersistAsync{TEvent}(TEvent,System.Action{TEvent})"/> 
    /// or <see cref="Eventsourced.DeferAsync{TEvent}"/> method calls.
    /// </summary>
    internal sealed class AsyncHandlerInvocation : IPendingHandlerInvocation
    {
        public AsyncHandlerInvocation(object evt, Action<object> handler)
        {
            Event = evt;
            Handler = handler;
        }

        public object Event { get; }

        public Action<object> Handler { get; }
    }

    /// <summary>
    /// Message used to detect that recovery timed out
    /// </summary>
    internal sealed class RecoveryTick
    {
        public RecoveryTick(bool snapshot)
        {
            Snapshot = snapshot;
        }

        public bool Snapshot { get; }
    }

    internal delegate void StateReceive(Receive receive, object message);

    internal class EventsourcedState
    {
        public EventsourcedState(string name, bool isRecoveryRunning, StateReceive stateReceive)
        {
            Name = name;
            IsRecoveryRunning = isRecoveryRunning;
            StateReceive = stateReceive;
        }

        public string Name { get; private set; }

        public bool IsRecoveryRunning { get; private set; }

        public StateReceive StateReceive { get; private set; }

        public override string ToString()
        {
            return Name;
        }
    }
}
