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

    [Fact(DisplayName = "DependencyInjection should create actor with IWithStash interface")]
    public void WithStashActorTest()
    {
        var stashActor = Sys.ActorOf(DependencyResolver.For(Sys).Props<WithStashActor>());
        
        stashActor.Tell(GetName.Instance, TestActor);
        ExpectNoMsg(0.3.Seconds());
        stashActor.Tell(GetName.Instance, TestActor);
        ExpectNoMsg(0.3.Seconds());
        
        stashActor.Tell(StartProcessing.Instance, TestActor);
        ExpectMsg<string>().Should().StartWith("s");
        ExpectMsg<string>().Should().StartWith("s");
        ExpectNoMsg(0.3.Seconds());
    }
    
    [Fact(DisplayName = "DependencyInjection should create actor with IWithUnboundedStash interface")]
    public void WithUnboundedStashActorTest()
    {
        var stashActor = Sys.ActorOf(DependencyResolver.For(Sys).Props<WithUnboundedStashActor>());
        
        stashActor.Tell(GetName.Instance, TestActor);
        ExpectNoMsg(0.3.Seconds());
        stashActor.Tell(GetName.Instance, TestActor);
        ExpectNoMsg(0.3.Seconds());
        
        stashActor.Tell(StartProcessing.Instance, TestActor);
        ExpectMsg<string>().Should().StartWith("s");
        ExpectMsg<string>().Should().StartWith("s");
        ExpectNoMsg(0.3.Seconds());
    }
    
    private sealed class WithStashActor: StashingActor, IWithStash
    {
        public WithStashActor(AkkaDiFixture.IScopedDependency scoped) : base(scoped)
        {
        }
    }
    
    private sealed class WithUnboundedStashActor: StashingActor, IWithUnboundedStash
    {
        public WithUnboundedStashActor(AkkaDiFixture.IScopedDependency scoped) : base(scoped)
        {
        }
    }
    
    private abstract class StashingActor : ReceiveActor
    {
        private readonly AkkaDiFixture.IScopedDependency _scoped;

        protected StashingActor(AkkaDiFixture.IScopedDependency scoped)
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