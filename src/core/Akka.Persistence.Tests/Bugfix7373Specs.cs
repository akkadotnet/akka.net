//-----------------------------------------------------------------------
// <copyright file="Bugfix7373Specs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Tests;

public class Bugfix7373Specs : AkkaSpec
{
    public Bugfix7373Specs(ITestOutputHelper output) : base(output)
    {
    }
    
    /// <summary>
    /// Reproduction for https://github.com/akkadotnet/akka.net/issues/7373 
    /// </summary>
    [Fact]
    public async Task ShouldDeliverAllStashedMessages()
    {
        // arrange
        var actor = Sys.ActorOf(Props.Create<MinimalStashingActor>());
        
        // act
        var msg = new Msg(1);
        actor.Tell(msg);
        actor.Tell(msg);
        
        actor.Tell("Initialize");
        
        // assert
        await ExpectMsgAsync($"Processed: {msg}");
        await ExpectMsgAsync($"Processed: {msg}");
    }
    
    public sealed record Msg(int Id);
    
    public class MinimalStashingActor : UntypedPersistentActor, IWithStash
    {
        public override string PersistenceId => "minimal-stashing-actor";

        protected override void OnCommand(object message)
        {
            Sender.Tell($"Processed: {message}");
        }

        private void Ready(object message)
        {
            switch (message)
            {
                case "Initialize":
                    Persist("init", e =>
                    {
                        Stash.UnstashAll(); // Unstash all stashed messages
                        Become(OnCommand); // Transition to ready state
                    });
                    break;
                default:
                    Stash.Stash(); // Stash messages until initialized
                    break;
            }
        }

        protected override void OnRecover(object message)
        {
            switch (message)
            {
                case RecoveryCompleted:
                    Become(Ready);
                    break;
            }
        }
    }
}
