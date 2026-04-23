//-----------------------------------------------------------------------
// <copyright file="AkkaCleanAmbientContextAttribute.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
/// Applied to <see cref="TestKit"/> (and inherited by derived test
/// classes) so users get parallel-safe behavior automatically. See
/// <see cref="ActorCellKeepingSynchronizationContext"/> for the underlying
/// mechanism and the ThreadStatic-vs-ExecutionContext rationale.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AkkaCleanAmbientContextAttribute : BeforeAfterTestAttribute
{
    [ThreadStatic]
    private static SynchronizationContext? _previousContext;

    [ThreadStatic]
    private static bool _applied;

    /// <inheritdoc/>
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        var instance = TestContext.Current.TestClassInstance;
        if (instance is not TestKitBase testKit)
        {
            _applied = false;
            return;
        }

        _applied = true;

        // Null cell for INoImplicitSender mirrors TestKitBase.InitializeTest:
        // the Post wrapper will pin Current = null so no sibling cell leaks in.
        var cell = testKit is INoImplicitSender ? null : TryGetCell(testKit);

        InternalCurrentActorCellKeeper.Current = cell;
        _previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new ActorCellKeepingSynchronizationContext(cell, _previousContext));
    }

    /// <inheritdoc/>
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (!_applied)
            return;

        _applied = false;
        InternalCurrentActorCellKeeper.Current = null;
        SynchronizationContext.SetSynchronizationContext(_previousContext);
        _previousContext = null;
    }

    private static ActorCell? TryGetCell(TestKitBase testKit)
    {
        return testKit.TestActor is ActorRefWithCell withCell
            ? withCell.Underlying as ActorCell
            : null;
    }
}
