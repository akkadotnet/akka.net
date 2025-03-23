//-----------------------------------------------------------------------
// <copyright file="Bugfix7398Specs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Actor.Stash;

public class Bugfix7398Specs : AkkaSpec
{
    public Bugfix7398Specs(ITestOutputHelper output)
        : base(output)
    {
    }

    private class IllegalStashActor : UntypedActor, IWithStash
    {
        protected override void OnReceive(object message)
        {
            
        }

        protected override void PreStart()
        {
            // ILLEGAL
            Stash.Stash();
        }

        public IStash Stash { get; set; }
    }
    
    [Fact]
    public void Should_throw_exception_when_stashing_in_PreStart()
    {
        EventFilter.Exception<ActorInitializationException>().ExpectOne(() =>
        {
            var actor = Sys.ActorOf(Props.Create<IllegalStashActor>());
            actor.Tell("hello");
        });
    }
}
