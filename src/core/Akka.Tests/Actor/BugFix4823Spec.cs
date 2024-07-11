//-----------------------------------------------------------------------
// <copyright file="BugFix4823Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit.Abstractions;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Actor
{
    public class BugFix4823Spec : AkkaSpec
    {
        public BugFix4823Spec(ITestOutputHelper outputHelper) : base(outputHelper)
        {
        }

        [Fact]
        public async Task Actor_should_not_loose_self_context_after_async_call()
        {
            var identity = ActorOfAsTestActorRef<MyActor>(Props.Create(() => new MyActor(TestActor)), TestActor);
            identity.Tell(NotUsed.Instance);
            var selfBefore = await ExpectMsgAsync<IActorRef>();
            var selfAfter = await ExpectMsgAsync<IActorRef>();
            selfAfter.Should().Be(selfBefore);
        }

        class MyActor : ReceiveActor
        {
            public MyActor(IActorRef testActor)
            {
                ReceiveAnyAsync(async _ =>
                {
                    testActor.Tell(Self);
                    await Task.Delay(100);
                    testActor.Tell(Self);
                });
            }
        }
    }
}
