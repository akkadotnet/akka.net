// -----------------------------------------------------------------------
//  <copyright file="Bugfix7501Specs.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;

namespace Akka.Tests.Actor;

public class Bugfix7501Specs : AkkaSpec
{
    public Bugfix7501Specs(ITestOutputHelper output) : base(output)
    {
        
    }

    [Fact]
    public async Task FutureActorRefShouldSupportDeathWatch()
    {
        // arrange
        var customDeathWatchProbe = CreateTestProbe();
        var watcher = Sys.ActorOf(act =>
        {
            act.Receive<string>((_, context) =>
            {
                // complete the Ask
                context.Sender.Tell("hi");

                // DeathWatch the FutureActorRef<T> BEFORE it completes
                context.Watch(context.Sender);
                
                // deliver the IActorRef of the Ask-er to TestActor
                TestActor.Tell(context.Sender);
            });
            
            act.Receive<Terminated>((terminated, context) =>
            {
                // shut ourselves down to signal that we got our Terminated from FutureActorRef
                context.Stop(context.Self);
            });
        });

        // act
        await customDeathWatchProbe.WatchAsync(watcher);
        await watcher.Ask<string>("boo", RemainingOrDefault);
        var futureActorRef = await ExpectMsgAsync<IActorRef>();
        await WatchAsync(futureActorRef); // Ask is finished - should immediately dead-letter
        
        // assert
        await ExpectTerminatedAsync(futureActorRef);
        
        // get the DeathWatch notification from the original actor
        // this can only be received if the original actor got a Terminated message from FutureActorRef
        await customDeathWatchProbe.ExpectTerminatedAsync(watcher);
    }
}