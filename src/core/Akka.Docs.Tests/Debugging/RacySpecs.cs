//-----------------------------------------------------------------------
// <copyright file="RacySpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.TestKit.Xunit2;
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
            
        }
        // </PoorMsgOrdering>

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
    }
}