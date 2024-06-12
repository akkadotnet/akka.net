// -----------------------------------------------------------------------
//  <copyright file="7247BugFixSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
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
        var noCellPath = new RootActorPath(Address.AllSystems);
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var noCellRef = new NoCellActorRef(noCellPath, tcs);
        
        // act
        var msg = "hit";
        Sys.Scheduler.ScheduleTellOnce(TimeSpan.FromMicroseconds(1), noCellRef, msg, ActorRefs.NoSender);
        var respMsg = await tcs.Task;
        
        // assert
        Assert.Equal(msg, respMsg);
    }
}