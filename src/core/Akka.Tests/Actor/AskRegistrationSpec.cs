// -----------------------------------------------------------------------
//  <copyright file="AskRegistrationSpec.cs" company="Akka.NET Project">
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

public sealed class AskRegistrationSpec : AkkaSpec
{
    public AskRegistrationSpec(ITestOutputHelper output) : base(output)
    {
    }

    [Theory(DisplayName = "Ask should remove its temporary reference when the token is already canceled")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_unregister_temp_actor_when_token_is_already_canceled(bool finiteTimeout)
    {
        var target = CreateTestProbe();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = target.Ref.Ask<string>("request",
            finiteTimeout ? TimeSpan.FromSeconds(30) : Timeout.InfiniteTimeSpan,
            cancellation.Token);

        await Assert.ThrowsAsync<TaskCanceledException>(() => result);
        await target.ExpectMsgAsync("request", TimeSpan.FromSeconds(3));
        var future = Assert.IsType<FutureActorRef<string>>(target.Sender);
        var provider = ((ExtendedActorSystem)Sys).Provider;

        await AwaitAssertAsync(() => Assert.NotSame(future, provider.ResolveActorRef(future.Path)));
    }
}
