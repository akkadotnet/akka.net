//-----------------------------------------------------------------------
// <copyright file="BugFix4823Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Actor
{
    /// <summary>
    /// Spec for https://github.com/akkadotnet/akka.net/issues/4376
    /// </summary>
    public class BugFix4823Spec : AkkaSpec
    {
        public BugFix4823Spec(ITestOutputHelper outputHelper) : base(outputHelper)
        {
        }

        [Fact]
        public void TestActorRef_should_not_lose_self_context_after_async_call()
        {
            var identity = ActorOfAsTestActorRef<MyActor>(Props.Create(() => new MyActor(TestActor)), TestActor);
            identity.Tell(NotUsed.Instance);
            var selfBefore = ExpectMsg<IActorRef>();
            var selfAfter = ExpectMsg<IActorRef>();
            selfAfter.Should().Be(selfBefore);
        }
        
        [Fact]
        public void ActorRef_should_not_lose_self_context_after_async_call()
        {
            var identity = Sys.ActorOf(Props.Create(() => new MyActor(TestActor)));
            identity.Tell(NotUsed.Instance);
            var selfBefore = ExpectMsg<IActorRef>();
            var selfAfter = ExpectMsg<IActorRef>();
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