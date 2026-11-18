//-----------------------------------------------------------------------
// <copyright file="FSMSpecHelpers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.TestKit
{
    /// <summary>
    /// Creates comparison delegates for FSM state messages in tests.
    /// </summary>
    public static class FSMSpecHelpers
    {
        /// <summary>
        /// Creates a comparer for current-state notifications from FSM actors.
        /// </summary>
        /// <typeparam name="TS">The type of the FSM state name.</typeparam>
        /// <returns>A delegate that compares current-state messages by actor reference and state name.</returns>
        public static Func<object, object, bool> CurrentStateExpector<TS>()
        {
            return (expected, actual) =>
            {
                var expectedFsmState = expected.AsInstanceOf<FSMBase.CurrentState<TS>>();
                var actualFsmState = actual.AsInstanceOf<FSMBase.CurrentState<TS>>();
                return expectedFsmState.FsmRef.Equals(actualFsmState.FsmRef) &&
                       expectedFsmState.State.Equals(actualFsmState.State);
            };
        }

        /// <summary>
        /// Creates a comparer for transition notifications from FSM actors.
        /// </summary>
        /// <typeparam name="TS">The type of the FSM state name.</typeparam>
        /// <returns>A delegate that compares transition messages by actor reference and their source and destination states.</returns>
        public static Func<object, object, bool> TransitionStateExpector<TS>()
        {
            return (expected, actual) =>
            {
                var expectedFsmState = expected.AsInstanceOf<FSMBase.Transition<TS>>();
                var actualFsmState = actual.AsInstanceOf<FSMBase.Transition<TS>>();
                return expectedFsmState.FsmRef.Equals(actualFsmState.FsmRef) &&
                       expectedFsmState.To.Equals(actualFsmState.To) &&
                       expectedFsmState.From.Equals(actualFsmState.From);
            };
        } 
    }
}
