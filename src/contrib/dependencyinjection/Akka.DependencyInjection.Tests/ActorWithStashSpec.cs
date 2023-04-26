//-----------------------------------------------------------------------
// <copyright file="ActorWithStashSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DependencyInjection.Tests;

public class ActorWithStashSpec: AkkaSpec, IClassFixture<AkkaDiFixture>
{
    public ActorWithStashSpec(AkkaDiFixture fixture, ITestOutputHelper output) 
        : base(
            DependencyResolverSetup.Create(fixture.Provider)
                .And(BootstrapSetup.Create().WithConfig(TestKitBase.DefaultConfig)), 
            output)
    {
    }

    [Fact(DisplayName = "DependencyInjection should create actor with stash")]
    public void StashActorTest()
    {
        var stashActor = Sys.ActorOf(DependencyResolver.For(Sys).Props<StashingActor>());
        
        stashActor.Tell(GetName.Instance, TestActor);
        ExpectNoMsg(0.3.Seconds());
        stashActor.Tell(GetName.Instance, TestActor);
        ExpectNoMsg(0.3.Seconds());
        
        stashActor.Tell(StartProcessing.Instance, TestActor);
        ExpectMsg<string>().Should().StartWith("s");
        ExpectMsg<string>().Should().StartWith("s");
        ExpectNoMsg(0.3.Seconds());
    }
    
    private sealed class StashingActor : ReceiveActor, IWithStash
    {
        private readonly AkkaDiFixture.IScopedDependency _scoped;
        
        public StashingActor(AkkaDiFixture.IScopedDependency scoped)
        {
            _scoped = scoped;
            Become(Stashing);
        }

        private bool Stashing(object message)
        {
            if (message is StartProcessing)
            {
                Become(Processing);
                Stash.UnstashAll();
                return true;
            }
            
            Stash.Stash();
            return true;
        }

        private bool Processing(object message)
        {
            if (message is GetName)
            {
                Sender.Tell(_scoped.Name);
                return true;
            }

            return false;
        }
        
        public IStash Stash { get; set; }
    }
    
    private sealed class GetName
    {
        public static readonly GetName Instance = new();
        private GetName() { }
    }
    
    private sealed class StartProcessing
    {
        public static readonly StartProcessing Instance = new();
        private StartProcessing() { }
    }
}