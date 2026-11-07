//-----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using ActorTestKit = Akka.TestKit.Xunit.TestKit;

namespace Akka.Remote.Tests.Serialization;

/// <summary>Real JIT remote deployment with opposite write bindings, in one process and one binary version.</summary>
[Collection(DynamicTypeLoadingCollection.Name)]
public sealed class DaemonMsgCreateV2RemotingSpec : AkkaSpec
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly ITestOutputHelper _output;

    public DaemonMsgCreateV2RemotingSpec(ITestOutputHelper output) : base(output) => _output = output;

    [Theory(DisplayName = "Should_DeployActorsInBothDirections_When_RemoteSystemsUseOppositeWriters")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_DeployActorsInBothDirections_When_RemoteSystemsUseOppositeWriters(bool artery)
    {
        var transport = artery ? "artery" : "classic";
        await using var legacy = new ActorTestKit(RemoteConfig(artery, false), $"daemon-{transport}-legacy", _output);
        try
        {
            await using var v2 = new ActorTestKit(RemoteConfig(artery, true), $"daemon-{transport}-v2", _output);
            try
            {
                foreach (var pair in new[] { (Source: legacy, Target: v2, Id: 3), (Source: v2, Target: legacy, Id: 43) })
                {
                    var address = ((ExtendedActorSystem)pair.Target.Sys).Provider.DefaultAddress;
                    address.Port.Should().BeGreaterThan(0);
                    var props = Props.Create(typeof(ReportingActor), "constructed", 17)
                        .WithDeploy(Deploy.None.WithScope(new RemoteScope(address)));
                    var deployment = new DaemonMsgCreate(props, props.Deploy, "/remote/check", pair.Source.TestActor);
                    pair.Source.Sys.Serialization.FindSerializerFor(deployment).Identifier.Should().Be(pair.Id);
                    pair.Target.Sys.Serialization.GetSerializerById(3).Identifier.Should().Be(3);
                    pair.Target.Sys.Serialization.GetSerializerById(43).Identifier.Should().Be(43);
                    var remote = pair.Source.Sys.ActorOf(props, $"remote-{pair.Id}");
                    remote.Should().BeOfType<RemoteActorRef>();
                    var probe = pair.Source.CreateTestProbe();
                    remote.Tell("report", probe.Ref);
                    var report = await probe.ExpectMsgAsync<string>(Timeout);
                    report.Should().Be($"constructed:17:{pair.Target.Sys.Name}", "the actor was constructed on the destination system");
                    await pair.Source.WatchAsync(remote);
                    pair.Source.Sys.Stop(remote);
                    await pair.Source.ExpectTerminatedAsync(remote, Timeout);
                }
            }
            finally
            {
                await v2.ShutdownAsync(Timeout, verifySystemShutdown: true);
            }
        }
        finally
        {
            await legacy.ShutdownAsync(Timeout, verifySystemShutdown: true);
        }
    }

    private static Config RemoteConfig(bool artery, bool v2) => ConfigurationFactory.ParseString($$"""
        akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
        akka.actor.serialization-v2 = {{(v2 ? "on" : "off")}}
        akka.remote.dot-netty.tcp {
            hostname = "127.0.0.1"
            port = 0
        }
        akka.remote.artery {
            enabled = {{(artery ? "on" : "off")}}
            canonical.hostname = "127.0.0.1"
            canonical.port = 0
        }
        """);

    public sealed class ReportingActor : ReceiveActor
    {
        public ReportingActor(string text, int number)
        {
            Receive<string>(_ => Sender.Tell($"{text}:{number}:{Context.System.Name}"));
        }
    }
}
