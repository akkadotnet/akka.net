//-----------------------------------------------------------------------
// <copyright file="FSMSpecHelpers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.TestKit
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class FSMSpecHelpers
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TS">TBD</typeparam>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        /// <typeparam name="TS">TBD</typeparam>
        /// <returns>TBD</returns>
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
