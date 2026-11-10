// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Pattern;
using Akka.Remote;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.StreamRef;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using ActorTestKit = Akka.TestKit.Xunit.TestKit;

namespace Akka.Streams.Tests.Serialization;

public sealed class StreamRefProtocolV2RemotingSpec : AkkaSpec
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private readonly ITestOutputHelper _output;

    public StreamRefProtocolV2RemotingSpec(ITestOutputHelper output) : base(output)
    {
        _output = output;
    }

    [Theory(DisplayName = "Should_StreamOrderedElementsThroughSourceAndSinkRefs_When_PeersUseOpposingBindings")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_StreamOrderedElementsThroughSourceAndSinkRefs_When_PeersUseOpposingBindings(bool artery)
    {
        await using var legacy = new ActorTestKit(ConfigFor(artery, false), "stream-legacy", _output);
        await using var modern = new ActorTestKit(ConfigFor(artery, true), "stream-v2", _output);
        try
        {
            foreach (var (origin, receiver, writerId) in new[] { (legacy, modern, 30), (modern, legacy, 70) })
            {
                origin.Sys.Serialization.FindSerializerFor(new SequencedOnNext(0, 1)).Identifier.Should().Be(writerId);
                origin.Sys.Serialization.FindSerializerFor(new CumulativeDemand(1)).Identifier.Should().Be(writerId);
                origin.Sys.ActorOf(Props.Create(() => new StreamFactory()), "source");
                var address = ((ExtendedActorSystem)origin.Sys).Provider.DefaultAddress;
                var remote = await receiver.Sys.ActorSelection(new RootActorPath(address) / "user" / "source").ResolveOne(Timeout);
                remote.Should().BeOfType<RemoteActorRef>();
                var reference = await remote.Ask<ISourceRef<int>>("source", Timeout);
                var receiverWriterId = writerId == 30 ? 70 : 30;
                receiver.Sys.Serialization.FindSerializerFor(reference).Identifier.Should().Be(receiverWriterId);
                var values = await reference.Source.RunWith(Sink.Seq<int>(), receiver.Sys.Materializer()).WaitAsync(Timeout);
                values.Should().Equal(Enumerable.Range(1, 256));

                var probe = receiver.CreateTestProbe();
                remote.Tell("sink", probe.Ref);
                var sinkReference = await probe.ExpectMsgAsync<ISinkRef<int>>(Timeout);
                receiver.Sys.Serialization.FindSerializerFor(sinkReference).Identifier.Should().Be(receiverWriterId);
                Source.From(Enumerable.Range(1, 256)).RunWith(sinkReference.Sink, receiver.Sys.Materializer());
                for (var expected = 1; expected <= 256; expected++)
                    (await probe.ExpectMsgAsync<int>(Timeout)).Should().Be(expected);
                await probe.ExpectMsgAsync("sink-complete", Timeout);
            }
        }
        finally
        {
            await modern.ShutdownAsync(Timeout, verifySystemShutdown: true);
            await legacy.ShutdownAsync(Timeout, verifySystemShutdown: true);
        }
    }

    private static Config ConfigFor(bool artery, bool v2) => ConfigurationFactory.ParseString($$"""
        akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
        akka.actor.serialization-v2 = {{(v2 ? "on" : "off")}}
        akka.remote.dot-netty.tcp { hostname = "127.0.0.1", port = 0 }
        akka.remote.artery {
            enabled = {{(artery ? "on" : "off")}}
            canonical.hostname = "127.0.0.1"
            canonical.port = 0
        }
        """).WithFallback(ActorMaterializer.DefaultConfig());

    private sealed class StreamFactory : ReceiveActor
    {
        public StreamFactory()
        {
            Receive<string>(request =>
            {
                var materializer = Context.System.Materializer();
                if (request == "source")
                    Source.From(Enumerable.Range(1, 256)).RunWith(StreamRefs.SourceRef<int>(), materializer).PipeTo(Sender);
                else if (request == "sink")
                    StreamRefs.SinkRef<int>()
                        .To(Sink.ActorRef<int>(Sender, "sink-complete", error => new Status.Failure(error)))
                        .Run(materializer).PipeTo(Sender);
                else
                    throw new ArgumentException($"Unknown stream factory request [{request}].", nameof(request));
            });
        }
    }
}
