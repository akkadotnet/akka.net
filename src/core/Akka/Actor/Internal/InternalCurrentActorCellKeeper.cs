//-----------------------------------------------------------------------
// <copyright file="InternalCurrentActorCellKeeper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#nullable enable
using System;
using System.Threading;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    public static class InternalCurrentActorCellKeeper
    {
        // Dispatcher-owned slot. Mailbox.Run, ActorCell.UseThreadContext, and
        // InternalTestActorRef save/restore this around message processing.
        // Per-thread, zero allocation, does not flow across awaits.
        [ThreadStatic]
        private static ActorCell? _current;

        // Ambient slot for test/host code that needs the "current" cell to
        // flow across awaits. Test kits install a closure here; the closure
        // resolves the current TestActor cell lazily so that hosts whose
        // TestActor isn't available at ctor time (e.g. Akka.Hosting.TestKit,
        // where the ActorSystem is built asynchronously by IHost) can still
        // install the resolver synchronously in the ctor and have the
        // AsyncLocal flow its value into every subsequent test-body await.
        private static readonly AsyncLocal<Func<ActorCell?>?> _ambientResolver = new();

        /// <summary>
        /// Dispatcher-owned current cell. Reads and writes touch only the
        /// per-thread <c>[ThreadStatic]</c> slot. Callers that do the
        /// <c>var tmp = Current; Current = x; try { ... } finally { Current = tmp; }</c>
        /// save/restore pattern must continue to use this property so that
        /// an ambient (AsyncLocal) value does not leak into the ThreadStatic
        /// slot via the restore step.
        ///
        /// INTERNAL!
        /// </summary>
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        public static ActorCell? Current
        {
            get => _current;
            set => _current = value;
        }

        /// <summary>
        /// Returns the dispatcher-owned cell if a message is currently being
        /// processed on this thread, otherwise falls back to the test kit's
        /// ambient cell (which flows with ExecutionContext across awaits).
        ///
        /// Use this for implicit-sender resolution (e.g.
        /// <see cref="ActorCell.GetCurrentSelfOrNoSender"/>), NOT for
        /// save/restore — the hybrid value must never be written back to the
        /// ThreadStatic slot, or cross-test leaks become possible.
        ///
        /// INTERNAL!
        /// </summary>
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        public static ActorCell? CurrentOrAmbient
        {
            get
            {
                var dispatcherCurrent = _current;
                if (dispatcherCurrent is not null)
                    return dispatcherCurrent;

                return _ambientResolver.Value?.Invoke();
            }
        }

        /// <summary>
        /// Ambient cell resolver. Flows with <see cref="ExecutionContext"/>
        /// across <c>await</c> points, so test-harness code can install a
        /// resolver in the test class constructor and have the test body
        /// read a consistent cell regardless of which ThreadPool thread a
        /// continuation resumes on.
        ///
        /// The resolver is invoked lazily on every <see cref="CurrentOrAmbient"/>
        /// read that doesn't hit the dispatcher slot, so test kits whose
        /// TestActor is constructed after the ctor (asynchronously during
        /// <c>IAsyncLifetime.InitializeAsync</c>) can install the resolver
        /// synchronously in the ctor and still resolve the correct cell
        /// once the TestActor is ready.
        ///
        /// Production code should not use this; pass the sender explicitly.
        ///
        /// INTERNAL!
        /// </summary>
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        public static Func<ActorCell?>? AmbientResolver
        {
            get => _ambientResolver.Value;
            set => _ambientResolver.Value = value;
        }
    }
}
