// -----------------------------------------------------------------------
// <copyright file="ImplicitSenderScopeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor;

public sealed class ImplicitSenderScopeSpec
{
    [Fact(DisplayName = "Should_resolve_logical_sender_without_installing_an_actor_cell")]
    public void Should_ResolveLogicalSender_WithoutInstallingActorCell()
    {
        var previous = InternalCurrentActorCellKeeper.Current;
        try
        {
            InternalCurrentActorCellKeeper.Current = null;
            using var scope = ImplicitSenderScope.Enter(() => ActorRefs.Nobody);
            Assert.Same(ActorRefs.Nobody, ActorCell.GetCurrentSelfOrNoSender());
            Assert.Null(ActorCell.GetCurrentSenderOrNoSender());
            Assert.Null(InternalCurrentActorCellKeeper.Current);
        }
        finally
        {
            InternalCurrentActorCellKeeper.Current = previous;
        }
    }

    [Fact(DisplayName = "Should_preserve_sender_after_a_nested_clean_execution_context")]
    public void Should_PreserveSender_AfterNestedCleanExecutionContext()
    {
        var clean = ExecutionContext.Capture();
        Assert.NotNull(clean);
        using var scope = ImplicitSenderScope.Enter(() => ActorRefs.Nobody);
        ExecutionContext.Run(clean, _ => Assert.Null(ImplicitSenderScope.Current), null);
        Assert.Same(ActorRefs.Nobody, ImplicitSenderScope.Current);
    }

    [Fact(DisplayName = "Should_mask_parent_with_an_active_null_scope")]
    public void Should_MaskParent_WithActiveNullScope()
    {
        using var parent = ImplicitSenderScope.Enter(() => ActorRefs.Nobody);
        using (ImplicitSenderScope.Enter(() => null))
            Assert.Null(ImplicitSenderScope.Current);
        Assert.Same(ActorRefs.Nobody, ImplicitSenderScope.Current);
    }

    [Fact(DisplayName = "Should_revoke_captured_child_without_restoring_its_parent")]
    public void Should_RevokeCapturedChild_WithoutRestoringItsParent()
    {
        using var parent = ImplicitSenderScope.Enter(() => ActorRefs.Nobody);
        var child = ImplicitSenderScope.Enter(() => ActorRefs.Nobody);
        var captured = ExecutionContext.Capture();
        Assert.NotNull(captured);
        child.Dispose();
        Assert.Same(ActorRefs.Nobody, ImplicitSenderScope.Current);
        ExecutionContext.Run(captured, _ => Assert.Null(ImplicitSenderScope.Current), null);
        Assert.Same(ActorRefs.Nobody, ImplicitSenderScope.Current);
    }

    [Fact(DisplayName = "Should_detach_caller_without_revoking_captured_work")]
    public void Should_DetachCaller_WithoutRevokingCapturedWork()
    {
        using var scope = ImplicitSenderScope.Enter(() => ActorRefs.Nobody);
        var captured = ExecutionContext.Capture();
        Assert.NotNull(captured);
        scope.Detach();
        Assert.Null(ImplicitSenderScope.Current);
        ExecutionContext.Run(captured, _ => Assert.Same(ActorRefs.Nobody, ImplicitSenderScope.Current), null);
        scope.Dispose();
        ExecutionContext.Run(captured, _ => Assert.Null(ImplicitSenderScope.Current), null);
    }

    [Fact(DisplayName = "Should_revoke_sender_in_an_already_suspended_child_task")]
    public async Task Should_RevokeSender_InSuspendedChildTask()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var scope = ImplicitSenderScope.Enter(() => ActorRefs.Nobody);
        var child = Task.Run(async () =>
        {
            Assert.Same(ActorRefs.Nobody, ImplicitSenderScope.Current);
            entered.SetResult(true);
            await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return ImplicitSenderScope.Current;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        scope.Dispose();
        release.SetResult(true);
        Assert.Null(await child.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact(DisplayName = "Should_not_flow_sender_when_execution_context_flow_is_suppressed")]
    public async Task Should_NotFlowSender_WhenExecutionContextFlowIsSuppressed()
    {
        using var scope = ImplicitSenderScope.Enter(() => ActorRefs.Nobody);
        Task<IActorRef?> isolated;
        using (ExecutionContext.SuppressFlow())
            isolated = Task.Run(() => ImplicitSenderScope.Current);
        Assert.Null(await isolated.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(ActorRefs.Nobody, ImplicitSenderScope.Current);
    }

    [Fact(DisplayName = "Should_read_recreated_sender_and_never_resurrect_after_disposal")]
    public void Should_ReadRecreatedSender_AndNeverResurrectAfterDisposal()
    {
        IActorRef? sender = null;
        using var scope = ImplicitSenderScope.Enter(() => Volatile.Read(ref sender));
        Assert.Null(ImplicitSenderScope.Current);
        Volatile.Write(ref sender, ActorRefs.Nobody);
        Assert.Same(ActorRefs.Nobody, ImplicitSenderScope.Current);
        scope.Dispose();
        Volatile.Write(ref sender, ActorRefs.Nobody);
        Assert.Null(ImplicitSenderScope.Current);
    }
}

public sealed class ImplicitSenderPrecedenceSpec(ITestOutputHelper output) : AkkaSpec(output)
{
    [Fact(DisplayName = "Should_prefer_the_current_actor_cell_over_a_logical_sender")]
    public void Should_PreferCurrentActorCell_OverLogicalSender()
    {
        Assert.NotNull(InternalCurrentActorCellKeeper.Current);
        using var scope = ImplicitSenderScope.Enter(() => Sys.DeadLetters);
        Assert.Same(TestActor, ActorCell.GetCurrentSelfOrNoSender());
    }
}
