//-----------------------------------------------------------------------
// <copyright file="ActorCellKeepingSynchronizationContext.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Annotations;

namespace Akka.TestKit
{
    /// <summary>
    /// INTERNAL API.
    /// <para/>
    /// A <see cref="SynchronizationContext"/> used by the test kits to pin
    /// the ambient <see cref="InternalCurrentActorCellKeeper.Current"/>
    /// <see cref="ActorCell"/> across <c>await</c> continuations that
    /// originate from a test body.
    /// <para/>
    /// <see cref="InternalCurrentActorCellKeeper.Current"/> is a
    /// <see cref="ThreadStaticAttribute"/> slot — it does not flow through
    /// <see cref="System.Threading.ExecutionContext"/>. When a test awaits,
    /// the continuation can resume on an arbitrary <see cref="ThreadPool"/>
    /// thread whose <see cref="ThreadStaticAttribute"/> slot is either empty
    /// or polluted by unrelated work. Installing this SC on the test-body
    /// thread causes every posted continuation to run inside a
    /// save/pin/restore window, so the test's cell is visible to
    /// <c>IActorRef.Tell(message)</c> implicit-sender resolution and
    /// to anything else reading <see cref="InternalCurrentActorCellKeeper.Current"/>
    /// from the continuation.
    /// <para/>
    /// Not intended for use outside the test kits.
    /// </summary>
    [InternalApi]
    public class ActorCellKeepingSynchronizationContext : SynchronizationContext
    {
        private readonly ActorCell _cell;

        /// <summary>
        /// Creates a new <see cref="ActorCellKeepingSynchronizationContext"/>
        /// that pins the given <paramref name="cell"/> as
        /// <see cref="InternalCurrentActorCellKeeper.Current"/> for the
        /// duration of every callback posted through it.
        /// </summary>
        /// <param name="cell">
        /// The <see cref="ActorCell"/> to pin as the ambient current cell,
        /// or <see langword="null"/> to pin "no implicit sender" (mirrors
        /// the <see cref="INoImplicitSender"/> branch of
        /// <see cref="TestKitBase.InitializeTest(ActorSystem, Akka.Actor.Setup.ActorSystemSetup, string, string)"/>).
        /// </param>
        public ActorCellKeepingSynchronizationContext(ActorCell cell)
        {
            _cell = cell;
        }

        /// <summary>
        /// Queues the given callback to run on the <see cref="ThreadPool"/>
        /// with <see cref="InternalCurrentActorCellKeeper.Current"/> pinned
        /// to the cell this SC was constructed with, then restores the
        /// previously pinned value when the callback returns.
        /// </summary>
        /// <param name="d">The delegate to invoke.</param>
        /// <param name="state">The state object to pass to <paramref name="d"/>.</param>
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
        /// Synchronously dispatches the given callback through
        /// <see cref="Post(SendOrPostCallback, object)"/>, blocking the
        /// calling thread until the callback completes or throws.
        /// </summary>
        /// <param name="d">The delegate to invoke.</param>
        /// <param name="state">The state object to pass to <paramref name="d"/>.</param>
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
