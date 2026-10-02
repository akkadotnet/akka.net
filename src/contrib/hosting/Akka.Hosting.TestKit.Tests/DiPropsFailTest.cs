// -----------------------------------------------------------------------
//  <copyright file="DiPropsFailTest.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Akka.Hosting.TestKit.Tests;

// Regression test for https://github.com/akkadotnet/Akka.Hosting/issues/343
public class DiPropsFailTest: TestKit
{
    public DiPropsFailTest(XunitTestOutputHelper output) : base(nameof(DiPropsFailTest), output)
    {}
    
    protected override void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        base.ConfigureServices(context, services);
        services.AddLogging();
    }
    
    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    { }

    [Fact]
    public void DiTest()
    {
        var actor = Sys.ActorOf(NonRootActorWithDi.Props(Sys));
        actor.Tell("test");
        ExpectMsg<string>("test");
    }
    
    private class NonRootActorWithDi: ReceiveActor
    {
        public static Props Props(ActorSystem system) => DependencyResolver.For(system).Props<NonRootActorWithDi>();
        
        public NonRootActorWithDi(ILogger<NonRootActorWithDi> log)
        {
            ReceiveAny(msg =>
            {
                log.LogInformation("Received {Msg}", msg);
                Sender.Tell(msg);
            });
        }
    }
}
