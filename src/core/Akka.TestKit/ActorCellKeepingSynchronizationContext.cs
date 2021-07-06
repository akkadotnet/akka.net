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
    /// INTERNAL API
    ///
    /// Used to resolve 
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

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override void Send(SendOrPostCallback d, object state)
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
        }
    }
}
