//-----------------------------------------------------------------------
// <copyright file="ActorCellKeepingSynchronizationContext.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    sealed class ActorCellKeepingSynchronizationContext : SynchronizationContext
    {
        private readonly ActorCell _cell;
        
        internal static ActorCell AsyncCache { get; set; }

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
            ThreadPool.QueueUserWorkItem(s =>
            {
                var t = ((SendOrPostCallback, object, ActorCellKeepingSynchronizationContext, ActorCell))s;

                var oldCell = InternalCurrentActorCellKeeper.Current;
                var oldContext = Current;
                SetSynchronizationContext(t.Item3);
                InternalCurrentActorCellKeeper.Current = t.Item4;

                try
                {
                    t.Item1(t.Item2);
                }
                finally
                {
                    InternalCurrentActorCellKeeper.Current = oldCell;
                    SetSynchronizationContext(oldContext);
                }
            }, (d, state, this, AsyncCache ?? _cell));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="d">TBD</param>
        /// <param name="state">TBD</param>
        public override void Send(SendOrPostCallback d, object state)
        {
            if(ReferenceEquals(Current, this))
            {
                var oldCell = InternalCurrentActorCellKeeper.Current;
                InternalCurrentActorCellKeeper.Current = AsyncCache ?? _cell;
                try
                {
                    d(state);
                }
                finally
                {
                    InternalCurrentActorCellKeeper.Current = oldCell;
                }
                return;
            }

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
