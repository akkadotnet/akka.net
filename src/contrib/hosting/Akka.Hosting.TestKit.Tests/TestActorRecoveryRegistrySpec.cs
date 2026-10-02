using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Hosting;
using Akka.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Akka.Hosting.TestKit.Tests;

file sealed class CachedProbeForwarder : ReceiveActor
{
    private readonly IActorRef _cachedProbe;

    public sealed class Send
    {
        public Send(string message)
        {
            Message = message;
        }

        public string Message { get; }
    }

    public sealed class GetCachedProbe
    {
        public static readonly GetCachedProbe Instance = new();

        private GetCachedProbe()
        {
        }
    }

    public CachedProbeForwarder(IRequiredActor<TestProbe> probe)
    {
        _cachedProbe = probe.ActorRef;

        Receive<Send>(send => _cachedProbe.Tell(send.Message));
        Receive<GetCachedProbe>(_ => Sender.Tell(_cachedProbe));
    }
}

sealed class RecoveryTestKit : Akka.Hosting.TestKit.TestKit
{
    public RecoveryTestKit(ITestOutputHelper output)
        : base($"recovery-{Guid.NewGuid():N}", output: output, startupTimeout: TimeSpan.FromSeconds(10), logLevel: LogLevel.Error)
    {
    }

    public IActorRef Forwarder => ActorRegistry.Get<CachedProbeForwarder>();

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder.WithActors((system, registry, resolver) =>
        {
            var forwarder = system.ActorOf(resolver.Props<CachedProbeForwarder>(), "cached-probe-forwarder");
            registry.Register<CachedProbeForwarder>(forwarder);
        });
    }
}

public class TestActorRecoveryRegistrySpec
{
    private readonly ITestOutputHelper _output;

    public TestActorRecoveryRegistrySpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Cached_required_TestProbe_reference_should_survive_TestActor_recovery()
    {
        await using var kit = new RecoveryTestKit(_output);
        await kit.InitializeAsync();

        var initialTestActor = kit.TestActor;
        var cachedProbe = await kit.Forwarder.Ask<IActorRef>(CachedProbeForwarder.GetCachedProbe.Instance, TimeSpan.FromSeconds(3));
        cachedProbe.Should().NotBe(ActorRefs.Nobody);
        cachedProbe.Should().NotBe(initialTestActor);

        kit.Forwarder.Tell(new CachedProbeForwarder.Send("before-recovery"));
        await kit.ExpectMsgAsync<string>("before-recovery", TimeSpan.FromSeconds(3));

        initialTestActor.Tell(PoisonPill.Instance);
        await WaitUntilProbeStopsAsync(kit, initialTestActor);

        await kit.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

        await kit.ForceReinitializeTestActorAsync();
        kit.TestActor.Should().NotBe(initialTestActor);

        kit.Forwarder.Tell(new CachedProbeForwarder.Send("after-recovery"));
        await kit.ExpectMsgAsync<string>("after-recovery", TimeSpan.FromSeconds(3));
    }

    private static async Task WaitUntilProbeStopsAsync(RecoveryTestKit kit, IActorRef probe)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await kit.Sys.ActorSelection(probe.Path).ResolveOne(TimeSpan.FromMilliseconds(150));
                await Task.Delay(50);
            }
            catch (ActorNotFoundException)
            {
                return;
            }
        }

        Assert.Fail($"Probe [{probe.Path}] did not terminate within the expected window.");
    }
}
