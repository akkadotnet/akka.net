//-----------------------------------------------------------------------
// <copyright file="AkkaCleanAmbientContextAttribute.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Reflection;
using System.Threading;
using Akka.Actor;
using Akka.Actor.Internal;
using Xunit;
using Xunit.v3;

namespace Akka.TestKit.Xunit.Attributes;

/// <summary>
/// Makes a test class parallel-safe under xUnit v3's parallel-collection
/// scheduling by pinning <see cref="InternalCurrentActorCellKeeper.Current"/>
/// to the running test's TestActor cell on the body thread, and installing
/// an <see cref="ActorCellKeepingSynchronizationContext"/> that re-pins the
/// cell across <c>await</c> continuations.
/// <para/>
/// Intended for xUnit v3 test kits built on <see cref="TestKitBase"/>.
/// Applied to <see cref="TestKit"/> (and inherited by derived test
/// classes) so users get parallel-safe behavior automatically, and can also
/// be applied by downstream test kits that derive directly from
/// <see cref="TestKitBase"/>. See
/// <see cref="ActorCellKeepingSynchronizationContext"/> for the underlying
/// mechanism and the ThreadStatic-vs-ExecutionContext rationale.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AkkaCleanAmbientContextAttribute : BeforeAfterTestAttribute
{
    private sealed record AmbientContextState
    {
        public required bool Applied { get; init; }
        public SynchronizationContext? PreviousContext { get; init; }
        public ActorCell? PreviousCell { get; init; }
    }

    // AsyncLocal flows across await boundaries via ExecutionContext, unlike [ThreadStatic].
    // This is critical because xUnit v3's runner awaits the test body between Before() and After(),
    // so After() can resume on a different thread than Before() ran on.
    private static readonly AsyncLocal<AmbientContextState?> _state = new();

    /// <inheritdoc/>
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        var instance = TestContext.Current.TestClassInstance;
        if (instance is not TestKitBase testKit)
        {
            _state.Value = new AmbientContextState
            {
                Applied = false,
                PreviousContext = SynchronizationContext.Current,
                PreviousCell = InternalCurrentActorCellKeeper.Current
            };
            return;
        }

        // Null cell for INoImplicitSender mirrors TestKitBase.InitializeTest:
        // the Post wrapper will pin Current = null so no sibling cell leaks in.
        var cell = testKit is INoImplicitSender ? null : TryGetCell(testKit);

        _state.Value = new AmbientContextState
        {
            Applied = true,
            PreviousContext = SynchronizationContext.Current,
            PreviousCell = InternalCurrentActorCellKeeper.Current
        };

        InternalCurrentActorCellKeeper.Current = cell;
        SynchronizationContext.SetSynchronizationContext(
            new ActorCellKeepingSynchronizationContext(cell, _state.Value.PreviousContext));
    }

    /// <inheritdoc/>
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        var state = _state.Value;
        if (state is null || !state.Applied)
            return;

        InternalCurrentActorCellKeeper.Current = state.PreviousCell;
        SynchronizationContext.SetSynchronizationContext(state.PreviousContext);
        _state.Value = null;
    }

    private static ActorCell? TryGetCell(TestKitBase testKit)
    {
        return testKit.TestActor is ActorRefWithCell withCell
            ? withCell.Underlying as ActorCell
            : null;
    }
}
