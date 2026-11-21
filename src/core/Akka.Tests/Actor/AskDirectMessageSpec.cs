// -----------------------------------------------------------------------
//  <copyright file="AskDirectMessageSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor;

public sealed class AskDirectMessageSpec : AkkaSpec
{
    private IActorRefProvider Provider => ((ExtendedActorSystem)Sys).Provider;

    public AskDirectMessageSpec(ITestOutputHelper output) : base(output) { }

    private sealed class InlineTarget(IActorRefProvider provider, Action<object?, IActorRef> handler) : MinimalActorRef
    {
        public override IActorRefProvider Provider => provider;
        public override ActorPath Path => provider.RootPath / "inline-target";
        public int Calls { get; private set; }
        public IActorRef? LastSender { get; private set; }

        protected override void TellInternal(object message, IActorRef sender)
        {
            Calls++;
            LastSender = sender;
            handler(message, sender);
        }
    }

    private sealed class UnsupportedTarget : ICanTell
    {
        public void Tell(object message, IActorRef sender) => throw new InvalidOperationException("Unexpected delivery");
    }

    private sealed class CallbackTarget(Action<object, IActorRef> handler) : ICanTell
    {
        public void Tell(object message, IActorRef sender) => handler(message, sender);
    }

    private static Task<string> Ask(ICanTell target, object? message, bool factory,
        TimeSpan? timeout = null, CancellationToken token = default) => factory
        ? target.Ask<string>(_ => message!, timeout, token)
        : target.Ask<string>(message!, timeout, token);

    private async Task Removed(IActorRef future) =>
        await AwaitAssertAsync(() => Assert.NotSame(future, Provider.ResolveActorRef(future.Path)));

    private List<IActorRef> TempRefs()
    {
        var refs = new List<IActorRef>();
        ((VirtualPathContainer)Provider.TempContainer).ForEachChild(refs.Add);
        return refs;
    }

    [Theory(DisplayName = "Should_RegisterAndDeliverExactMessage_When_UsingEitherAskEntry")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_register_and_deliver_exact_message_when_using_either_ask_entry(bool factory)
    {
        var request = new object();
        var target = new InlineTarget(Provider, (message, sender) =>
        {
            Assert.Same(request, message);
            Assert.IsType<FutureActorRef<string>>(sender);
            Assert.Same(sender, Provider.ResolveActorRef(sender.Path));
            sender.Tell("reply");
        });
        var result = Ask(target, request, factory, Timeout.InfiniteTimeSpan);
        Assert.Null(result.AsyncState);
        Assert.True((result.CreationOptions & TaskCreationOptions.RunContinuationsAsynchronously) != 0);
        Assert.Equal("reply", await result);
        Assert.Equal(1, target.Calls);
        await Removed(target.LastSender!);
    }

    [Fact(DisplayName = "Should_InvokeFactoryOnceAfterRegistration_WithTheActualReplyReference")]
    public async Task Should_invoke_factory_once_after_registration_with_the_actual_reply_reference()
    {
        var calls = 0;
        IActorRef? replyTo = null;
        var target = new InlineTarget(Provider, (message, sender) =>
        {
            Assert.Equal(1, calls);
            Assert.Same(replyTo, sender);
            Assert.Same(replyTo, message);
            sender.Tell("reply");
        });
        var result = target.Ask<string>(future =>
        {
            calls++;
            Assert.Equal(0, target.Calls);
            Assert.Same(future, Provider.ResolveActorRef(future.Path));
            replyTo = future;
            return future;
        }, Timeout.InfiniteTimeSpan, CancellationToken.None);
        Assert.Equal("reply", await result);
        await Removed(replyTo!);
    }

    [Theory(DisplayName = "Should_DeliverNullRequest_When_DirectMessageOrFactoryResultIsNull")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_deliver_null_request_when_direct_message_or_factory_result_is_null(bool factory)
    {
        var target = new InlineTarget(Provider, (message, sender) =>
        {
            Assert.Null(message);
            sender.Tell("reply");
        });
        Assert.Equal("reply", await Ask(target, null, factory, Timeout.InfiniteTimeSpan));
        await Removed(target.LastSender!);
    }

    // Characterize existing failure timing for this refactor; a separate cleanup fix may change it.
    [Theory(DisplayName = "Should_KeepNullFactoryDistinctFromDirectNull_AfterRegistration")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_keep_null_factory_distinct_from_direct_null_after_registration(bool canceled)
    {
        using var cancellation = new CancellationTokenSource();
        if (canceled) cancellation.Cancel();
        var target = new InlineTarget(Provider, (_, _) => throw new InvalidOperationException("Unexpected delivery"));
        // Logger startup also uses Ask; its temporary-ref cleanup can finish after initialization.
        await AwaitAssertAsync(() => Assert.Empty(TempRefs()));
        Assert.Throws<NullReferenceException>(() =>
        {
            _ = target.Ask<string>((Func<IActorRef, object>)null!, Timeout.InfiniteTimeSpan, cancellation.Token);
        });
        Assert.Equal(0, target.Calls);
        if (canceled)
        {
            await AwaitAssertAsync(() => Assert.Empty(TempRefs()));
            return;
        }
        var future = Assert.Single(TempRefs());
        // The original synchronous factory failure leaves cleanup attached to the pending result.
        future.Tell("finish");
        await Removed(future);
    }

    [Theory(DisplayName = "Should_StillSendAndCleanUp_When_TokenIsAlreadyCanceled")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Should_still_send_and_clean_up_when_token_is_already_canceled(bool factory, bool finite)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var target = new InlineTarget(Provider, (message, _) => Assert.Equal("request", message));
        var result = Ask(target, "request", factory, finite ? TimeSpan.FromSeconds(30) : Timeout.InfiniteTimeSpan,
            cancellation.Token);
        Assert.Equal(1, target.Calls);
        await Assert.ThrowsAsync<TaskCanceledException>(() => result);
        await Removed(target.LastSender!);
    }

    [Theory(DisplayName = "Should_CancelAndRetireTheSameFuture_When_CanceledAfterDelivery")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_cancel_and_retire_the_same_future_when_canceled_after_delivery(bool factory)
    {
        using var cancellation = new CancellationTokenSource();
        var target = new InlineTarget(Provider, (_, _) => { });
        var result = Ask(target, "request", factory, Timeout.InfiniteTimeSpan, cancellation.Token);
        Assert.Equal(1, target.Calls);
        Assert.False(result.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(() => result);
        await Removed(target.LastSender!);
    }

    [Theory(DisplayName = "Should_KeepTimeoutFailureAndCleanup_When_NoReplyArrives")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_keep_timeout_failure_and_cleanup_when_no_reply_arrives(bool factory)
    {
        var target = new InlineTarget(Provider, (_, _) => { });
        var result = Ask(target, "request", factory, TimeSpan.FromMilliseconds(50));
        var error = await Assert.ThrowsAsync<AskTimeoutException>(() => result);
        Assert.Equal("Timeout after 0.05 seconds", error.Message);
        Assert.Equal(1, target.Calls);
        await Removed(target.LastSender!);
    }

    // Preserve current synchronous failure/cleanup timing here rather than changing it during a refactor.
    [Theory(DisplayName = "Should_PreserveSynchronousDeliveryExceptionAndPendingCleanup")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_preserve_synchronous_delivery_exception_and_pending_cleanup(bool factory)
    {
        var error = new InvalidOperationException("delivery");
        var target = new InlineTarget(Provider, (_, _) => throw error);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
        {
            _ = Ask(target, "request", factory, Timeout.InfiniteTimeSpan);
        }));
        var future = target.LastSender!;
        Assert.Same(future, Provider.ResolveActorRef(future.Path));
        future.Tell("finish");
        await Removed(future);
    }

    // This characterizes current behavior, not a requirement to keep a pending ref after future fixes.
    [Fact(DisplayName = "Should_PreserveSynchronousFactoryExceptionAndPendingCleanup")]
    public async Task Should_preserve_synchronous_factory_exception_and_pending_cleanup()
    {
        var error = new InvalidOperationException("factory");
        IActorRef? created = null;
        var target = new InlineTarget(Provider, (_, _) => throw new InvalidOperationException("Unexpected delivery"));
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
        {
            _ = target.Ask<string>(future =>
            {
                created = future;
                Assert.Same(future, Provider.ResolveActorRef(future.Path));
                throw error;
            }, Timeout.InfiniteTimeSpan, CancellationToken.None);
        }));
        Assert.Equal(0, target.Calls);
        var retained = Assert.IsAssignableFrom<IActorRef>(created);
        retained.Tell("finish");
        await Removed(retained);
    }

    [Theory(DisplayName = "Should_ValidateProviderBeforeFactoryInvocation_When_TargetIsUnsupported")]
    [InlineData(false)]
    [InlineData(true)]
    public void Should_validate_provider_before_factory_invocation_when_target_is_unsupported(bool factory)
    {
        var called = false;
        var target = new UnsupportedTarget();
        var previous = InternalCurrentActorCellKeeper.Current;
        try
        {
            // TestKit installs an ambient cell; this case specifically exercises no provider.
            InternalCurrentActorCellKeeper.Current = null;
            var error = Assert.Throws<ArgumentException>(() =>
            {
                _ = factory
                    ? target.Ask<string>(_ => { called = true; return "request"; }, null, CancellationToken.None)
                    : target.Ask<string>("request", null, CancellationToken.None);
            });
            Assert.Equal("self", error.ParamName);
            Assert.False(called);
        }
        finally
        {
            InternalCurrentActorCellKeeper.Current = previous;
        }
    }
    [Theory(DisplayName = "Should_UseAmbientProvider_When_TargetOnlyImplementsCanTell")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_use_ambient_provider_when_target_only_implements_can_tell(bool factory)
    {
        IActorRef? future = null;
        var target = new CallbackTarget((message, sender) =>
        {
            Assert.Equal("request", message);
            Assert.Same(sender, Provider.ResolveActorRef(sender.Path));
            future = sender;
            sender.Tell("reply");
        });
        Task<string> result;
        var previous = InternalCurrentActorCellKeeper.Current;
        try
        {
            InternalCurrentActorCellKeeper.Current =
                Assert.IsAssignableFrom<ActorCell>(((ActorRefWithCell)TestActor).Underlying);
            result = Ask(target, "request", factory, Timeout.InfiniteTimeSpan);
        }
        finally
        {
            InternalCurrentActorCellKeeper.Current = previous;
        }
        Assert.Equal("reply", await result);
        await Removed(Assert.IsAssignableFrom<IActorRef>(future));
    }

}
