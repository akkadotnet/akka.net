//-----------------------------------------------------------------------
// <copyright file="RacySpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace DocsExamples.Debugging
{
    public class RacySpecs : TestKit
    {
        public RacySpecs(ITestOutputHelper output) : base(output: output)
        {
            
        }

        [Fact(Skip = "Buggy by design")]
        // <PoorMsgOrdering>
        public void PoorOrderingSpec()
        {
            IActorRef CreateForwarder(IActorRef actorRef)
            {
                return Sys.ActorOf(act =>
                {
                    act.ReceiveAny((o, context) =>
                    {
                        actorRef.Forward(o);
                    });
                });
            }

            // arrange
            IActorRef a1 = Sys.ActorOf(act => 
                act.Receive<string>((str, context) =>
                {
                    context.Sender.Tell(str + "a1");
                }), "a1");
            
            IActorRef a2 = CreateForwarder(a1);
            IActorRef a3 = CreateForwarder(a1);
            
            // act
            a2.Tell("hit1");
            a3.Tell("hit2");

            // assert
            
            /*
             * RACY: no guarantee that a2 gets scheduled ahead of a3.
             * That depends entirely upon the ThreadPool and the dispatcher.
             */
            
            ExpectMsg("hit1a1");
            ExpectMsg("hit2a1");
        }
        // </PoorMsgOrdering>
        
        [Fact]
        // <FixedMsgOrdering>
        public void FixedOrderingSpec()
        {
            IActorRef CreateForwarder(IActorRef actorRef)
            {
                return Sys.ActorOf(act =>
                {
                    act.ReceiveAny((o, context) =>
                    {
                        actorRef.Forward(o);
                    });
                });
            }

            // arrange
            IActorRef a1 = Sys.ActorOf(act => 
                act.Receive<string>((str, context) =>
                {
                    context.Sender.Tell(str + "a1");
                }), "a1");
            
            IActorRef a2 = CreateForwarder(a1);
            IActorRef a3 = CreateForwarder(a1);
            
            // act
            a2.Tell("hit1");
            a3.Tell("hit2");

            // assert

            // no raciness - ExpectMsgAllOf doesn't care about order
            ExpectMsgAllOf("hit1a1", "hit2a1");
        }
        // </FixedMsgOrdering>
        
        [Fact]
        // <SplitMsgOrdering>
        public void SplitOrderingSpec()
        {
            IActorRef CreateForwarder(IActorRef actorRef)
            {
                return Sys.ActorOf(act =>
                {
                    act.ReceiveAny((o, context) =>
                    {
                        actorRef.Forward(o);
                    });
                });
            }

            // arrange
            IActorRef a1 = Sys.ActorOf(act => 
                act.Receive<string>((str, context) =>
                {
                    context.Sender.Tell(str + "a1");
                }), "a1");

            TestProbe p2 = CreateTestProbe();
            IActorRef a2 = CreateForwarder(a1);
            TestProbe p3 = CreateTestProbe();
            IActorRef a3 = CreateForwarder(a1);
            
            // act
            
            // manually set the sender - one to each TestProbe
            a2.Tell("hit1", p2);
            a3.Tell("hit2", p3);

            // assert

            // no raciness - both probes can process their own messages in parallel
            p2.ExpectMsg("hit1a1");
            p3.ExpectMsg("hit2a1");
        }
        // </SplitMsgOrdering>

        [Fact(Skip = "Buggy by design")]
        // <PoorSysMsgOrdering>
        public void PoorSystemMessagingOrderingSpec()
        {
            // arrange
            var myActor = Sys.ActorOf(act => act.ReceiveAny((o, context) =>
            {
                context.Sender.Tell(o);
            }), "echo");
            
            // act
            Watch(myActor); // deathwatch
            myActor.Tell("hit");
            Sys.Stop(myActor);

            // assert
            ExpectMsg("hit");
            ExpectTerminated(myActor); // RACY
            /*
             * Sys.Stop sends a system message. If "echo" actor hasn't been scheduled to run yet,
             * then the Stop command might get processed first since system messages have priority.
             */
        }
        // </PoorSysMsgOrdering>
        
        [Fact]
        // <CorrectSysMsgOrdering>
        public void CorrectSystemMessagingOrderingSpec()
        {
            // arrange
            var myActor = Sys.ActorOf(act => act.ReceiveAny((o, context) =>
            {
                context.Sender.Tell(o);
            }), "echo");
            
            // act
            Watch(myActor); // deathwatch
            myActor.Tell("hit");

            // assert
            ExpectMsg("hit");
            
            Sys.Stop(myActor); // terminate after asserting processing
            ExpectTerminated(myActor);
        }
        // </CorrectSysMsgOrdering>
        
        [Fact]
        // <PoisonPillSysMsgOrdering>
        public void PoisonPillSystemMessagingOrderingSpec()
        {
            // arrange
            var myActor = Sys.ActorOf(act => act.ReceiveAny((o, context) =>
            {
                context.Sender.Tell(o);
            }), "echo");
            
            // act
            Watch(myActor); // deathwatch
            myActor.Tell("hit");
            
            // use PoisonPill to shut down actor instead;
            // eliminates raciness as it passes through /user
            // queue instead of /system queue.
            myActor.Tell(PoisonPill.Instance);

            // assert
            ExpectMsg("hit");
            ExpectTerminated(myActor); // works as expected
        }
        // </PoisonPillSysMsgOrdering>

        [Fact(Skip = "Racy by design")]
        // <TooTightTimingSpec>
        public void TooTightTimingSpec()
        {
            Task<IImmutableList<IEnumerable<int>>> t = Source.From(Enumerable.Range(1, 10))
                .GroupedWithin(1, TimeSpan.FromDays(1))
                .Throttle(1, TimeSpan.FromMilliseconds(110), 0, Akka.Streams.ThrottleMode.Shaping)
                .RunWith(Sink.Seq<IEnumerable<int>>(), Sys.Materializer());
            t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            t.Result.Should().BeEquivalentTo(Enumerable.Range(1, 10).Select(i => new List<int> {i}));
        }
        // </TooTightTimingSpec>
    }
}