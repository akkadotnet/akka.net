//-----------------------------------------------------------------------
// <copyright file="ActorCellKeepingSynchronizationContext.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;

namespace Akka.TestKit
{
    /// <summary>
    /// TBD
    /// </summary>
    class ActorCellKeepingSynchronizationContext : SynchronizationContext
    {
        private readonly ActorCell _cell;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cell">TBD</param>
        public ActorCellKeepingSynchronizationContext(ActorCell cell)
        {
            _cell = cell;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="d">TBD</param>
        /// <param name="state">TBD</param>
        public override void Post(SendOrPostCallback d, object state)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var oldCell = InternalCurrentActorCellKeeper.Current;
                var oldContext = Current;
                SetSynchronizationContext(this);
                InternalCurrentActorCellKeeper.Current = _cell;

                try
                {
                    d(state);
                }
                finally
                {
                    InternalCurrentActorCellKeeper.Current = oldCell;
                    SetSynchronizationContext(oldContext);
                }
            }, state);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="d">TBD</param>
        /// <param name="state">TBD</param>
        public override void Send(SendOrPostCallback d, object state)
        {
            var tcs = new TaskCompletionSource<int>();
            Post(_ =>
            {
                try
                {
                    d(state);
                    tcs.SetResult(0);
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
            }, state);
            tcs.Task.Wait();
        }
    }
}
