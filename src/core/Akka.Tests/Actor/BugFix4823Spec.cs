//-----------------------------------------------------------------------
// <copyright file="BugFix4823Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using System.Threading;

namespace Akka.Tests.Actor
{
    public class BugFix4823Spec : AkkaSpec
    {

        private static CancellationToken Token => TestContext.Current.CancellationToken;
        public BugFix4823Spec(ITestOutputHelper outputHelper) : base(outputHelper)
        {
        }

        [Fact]
        public async Task Actor_should_not_loose_self_context_after_async_call()
        {
            var identity = ActorOfAsTestActorRef<MyActor>(Props.Create(() => new MyActor(TestActor)), TestActor);
            identity.Tell(NotUsed.Instance);
            var selfBefore = await ExpectMsgAsync<IActorRef>(cancellationToken: Token);
            var selfAfter = await ExpectMsgAsync<IActorRef>(cancellationToken: Token);
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
