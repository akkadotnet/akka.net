// -----------------------------------------------------------------------
//  <copyright file="Bugfix8144Spec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.TestActors;
using Xunit;

namespace Akka.TestKit.Tests;

public class Bugfix8144Spec: Xunit.TestKit, IAsyncLifetime
{
    public ValueTask DisposeAsync()
    {
        return new ValueTask(Task.CompletedTask);
    }

    public async ValueTask InitializeAsync()
    {
        // Any await here can cause a thread switch
        await Task.Delay(TimeSpan.FromMilliseconds(100));
    }
    
    [Fact]
    public void Test_using_implicit_sender()
    {
        var actor = Sys.ActorOf(Props.Create<EchoActor>());
        
        // This uses ActorRefImplicitSenderExtensions.Tell which calls
        // ActorCell.GetCurrentSelfOrNoSender() — returns NoSender because
        // [ThreadStatic] InternalCurrentActorCellKeeper.Current is null
        // on this thread
        actor.Tell("hello");
        
        // Times out — response went to deadLetters, not TestActor
        ExpectMsg("hello");
    }    
}