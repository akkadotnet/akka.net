//-----------------------------------------------------------------------
// <copyright file="ActorTestServices.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.TestKit;
using Akka.Util.Internal;
using Microsoft.Extensions.Hosting;

namespace Akka.DependencyInjection.Tests;

internal class TestDiActor : ReceiveActor
{
    public static readonly AtomicCounter Counter = new(0);

    public TestDiActor(InjectedService injected)
    {
        long count = Counter.GetAndIncrement();
        Receive<GetMessage>(_ => Sender.Tell(new Message { Value = injected.Message, Counter = count }));
    }
}

internal class InjectedEchoActor : ReceiveActor
{
    public InjectedEchoActor(InjectedService injected, string thing)
    {
        Receive<string>(str => Sender.Tell(new Message { Value = injected.Message + thing, Counter = 0 }));
        Receive<int>(i => Sender.Tell(new Message { Value = injected.Message + thing, Counter = 0 }));
    }
}

internal class Message
{
    public string Value { get; set; }
    public long Counter { get; set; }
}

internal class GetMessage
{
    public static readonly GetMessage Instance = new();

    private GetMessage()
    {
    }
}

internal class InjectedService
{
    public string Message => "I was injected";
}

internal class CustomMailbox : UnboundedPriorityMailbox
{
    public CustomMailbox(Settings settings, Config config) : base(settings, config)
    {
    }

    protected override int PriorityGenerator(object message)
    {
        return message switch
        {
            string => 1,
            int => 2,
            _ => 3
        };
    }
}

internal class AkkaService : IHostedService
{
    public const string CustomMailboxName = "custom-mailbox";
    
    private readonly Config _customMailboxHocon = $$"""
                                                        custom-mailbox {
                                                            mailbox-type = "{{typeof(CustomMailbox).AssemblyQualifiedName}}"
                                                        }
                                                    """;

    public ActorSystem ActorSystem { get; private set; }

    private readonly IServiceProvider _serviceProvider;

    public AkkaService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var setup = DependencyResolverSetup.Create(_serviceProvider)
            .And(BootstrapSetup.Create().WithConfig(_customMailboxHocon.WithFallback(TestKitBase.DefaultConfig)));

        ActorSystem = ActorSystem.Create("TestSystem", setup);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await ActorSystem.Terminate();
    }
}
