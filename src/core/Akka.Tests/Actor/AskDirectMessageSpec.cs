// -----------------------------------------------------------------------
//  <copyright file="AskDirectMessageSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor;

public sealed class AskDirectMessageSpec : AkkaSpec
{
    public AskDirectMessageSpec(ITestOutputHelper output) : base(output) { }

    private sealed class InlineTarget(IActorRefProvider provider, Action<object?, IActorRef> handler) : MinimalActorRef
    {
        public override IActorRefProvider Provider => provider;
        public override ActorPath Path => provider.RootPath / "inline-target";
        public int Calls { get; private set; }
        protected override void TellInternal(object message, IActorRef sender)
        {
            Calls++;
            handler(message, sender);
        }
    }

    [Fact(DisplayName = "Should_InvokeFactoryOnceAfterRegistration_WithTheActualReplyReference")]
    public async Task Should_invoke_factory_once_after_registration_with_the_actual_reply_reference()
    {
        var provider = ((ExtendedActorSystem)Sys).Provider;
        var calls = 0;
        IActorRef? replyTo = null;
        var target = new InlineTarget(provider, (message, sender) =>
        {
            Assert.Equal(1, calls);
            Assert.Same(replyTo, sender);
            Assert.Same(replyTo, message);
            sender.Tell("reply");
        });
        Assert.Equal("reply", await target.Ask<string>(future =>
        {
            calls++;
            Assert.Equal(0, target.Calls);
            Assert.Same(future, provider.ResolveActorRef(future.Path));
            replyTo = future;
            return future;
        }, Timeout.InfiniteTimeSpan, CancellationToken.None));
        Assert.Equal(1, calls);
        Assert.Equal(1, target.Calls);
    }

    [Theory(DisplayName = "Should_DeliverNullRequest_When_DirectMessageOrFactoryResultIsNull")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_deliver_null_request_when_direct_message_or_factory_result_is_null(bool factory)
    {
        var target = new InlineTarget(((ExtendedActorSystem)Sys).Provider, (message, sender) =>
        {
            Assert.Null(message);
            sender.Tell("reply");
        });
        var result = factory
            ? target.Ask<string>(_ => null!, Timeout.InfiniteTimeSpan, CancellationToken.None)
            : target.Ask<string>((object)null!, Timeout.InfiniteTimeSpan);
        Assert.Equal("reply", await result);
        Assert.Equal(1, target.Calls);
    }

    [Fact(DisplayName = "Should_RejectNullFactory_WithoutDeliveringARequest")]
    public async Task Should_reject_null_factory_without_delivering_a_request()
    {
        var target = new InlineTarget(((ExtendedActorSystem)Sys).Provider, (_, _) => throw new InvalidOperationException("Unexpected delivery"));
        using var cancellation = new CancellationTokenSource();
        try
        {
            // The invalid factory differs from a valid factory returning null; exception type and timing are unspecified here.
            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await target.Ask<string>((Func<IActorRef, object>)null!, Timeout.InfiniteTimeSpan, cancellation.Token));
            Assert.Equal(0, target.Calls);
        }
        finally
        {
            cancellation.Cancel();
        }
    }
}
