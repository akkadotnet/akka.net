//-----------------------------------------------------------------------
// <copyright file="NoImplicitSenderSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Hosting.TestKit.Tests;

public class NoImplicitSenderSpec : TestKit, INoImplicitSender
{
    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        
    }

    [Fact]
    public void When_Not_ImplicitSender_then_testActor_is_not_sender()
    {
        var echoActor = Sys.ActorOf(c => c.ReceiveAny((m, ctx) => TestActor.Tell(ctx.Sender)));
        echoActor.Tell("message");
        var actorRef = ExpectMsg<IActorRef>();
        actorRef.Should().Be(Sys.DeadLetters);
    }

}

public class ImplicitSenderSpec : TestKit
{
    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        
    }

    [Fact]
    public void ImplicitSender_should_have_testActor_as_sender()
    {
        var echoActor = Sys.ActorOf(c => c.ReceiveAny((m, ctx) => TestActor.Tell(ctx.Sender)));
        echoActor.Tell("message");
        ExpectMsg<IActorRef>(actorRef => Equals(actorRef, TestActor));

        //Test that it works after we know that context has been changed
        echoActor.Tell("message");
        ExpectMsg<IActorRef>(actorRef => Equals(actorRef, TestActor));

    }


    [Fact]
    public void ImplicitSender_should_not_change_when_creating_Testprobes()
    {
        //Verifies that bug #459 has been fixed
        var testProbe = CreateTestProbe();
        TestActor.Tell("message");
        ReceiveOne();
        LastSender.Should().Be(TestActor);
    }

    [Fact]
    public void ImplicitSender_should_not_change_when_creating_TestActors()
    {
        var testActor2 = CreateTestActor("test2");
        TestActor.Tell("message");
        ReceiveOne();
        LastSender.Should().Be(TestActor);
    }
}