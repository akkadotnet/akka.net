//-----------------------------------------------------------------------
// <copyright file="BugFix7247Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Actor.Scheduler;

public class BugFix7247Spec : AkkaSpec {
    public sealed class NoCellActorRef : MinimalActorRef
    {
        public NoCellActorRef(ActorPath path, TaskCompletionSource<object> firstMessage)
        {
            Path = path;
            FirstMessage = firstMessage;
        }

        protected override void TellInternal(object message, IActorRef sender)
        {
            FirstMessage.TrySetResult(message);
        }

        public TaskCompletionSource<object> FirstMessage { get; }

        public override ActorPath Path { get; }
        public override IActorRefProvider Provider => throw new NotImplementedException();
    }

    [Fact(DisplayName = "Should not send ScheduledTellMsg envelopes to IActorRefs with no cell")]
    public async Task ShouldNotSendScheduledTellMsgToNoCellActorRefs()
    {
        // arrange
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var noCellRef = Sys.As<ExtendedActorSystem>().Provider.CreateFutureRef(tcs);
        var router = Sys.ActorOf(Props.Empty.WithRouter(new BroadcastGroup()));
        var routee = Routee.FromActorRef(noCellRef);
        router.Tell(new AddRoutee(routee));

        await AwaitAssertAsync(async () =>
        {
            var allRoutees = await router.Ask<Routees>(GetRoutees.Instance);
            allRoutees.Members.Count().ShouldBeGreaterThan(0);
        });
        
        // act
        var msg = "hit";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(0.5));
        Sys.Scheduler.ScheduleTellOnce(TimeSpan.FromMilliseconds(1), router, msg, ActorRefs.NoSender);
        await tcs.Task.WithCancellation(cts.Token); // will time out if we don't get our msg
        var respMsg = await tcs.Task;
        
        // assert
        Assert.Equal(msg, respMsg);
    }
}
