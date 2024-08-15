// -----------------------------------------------------------------------
//  <copyright file="StreamRefsSerializerSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;

namespace Akka.Streams.Tests;

public static class ActorSystem2
{
    public static ActorSystem CreateActorSystem()
    {
        var address = TestUtils.TemporaryServerAddress();
        var config = ConfigurationFactory.ParseString($@"        
            akka {{
              loglevel = INFO
              actor {{
                provider = remote
                serialize-messages = off
              }}
              remote.dot-netty.tcp {{
                port = {address.Port}
                hostname = ""{address.Address}""
              }}
            }}")
            .WithFallback(ConfigurationFactory.Default());

        var system = ActorSystem.Create("remote-system-2", config);

        return system;
    }
}

[Serializable]
public sealed class StartListening
{
}

[Serializable]
public sealed class RequestStream
{
    public RequestStream(IActorRef actorRef)
    {
        ActorRef = actorRef;
    }

    public IActorRef ActorRef { get; }
}

[Serializable]
public sealed class EnvelopedStream
{
    public EnvelopedStream(ISourceRef<string> sourceRef)
    {
        SourceRef = sourceRef;
    }

    public ISourceRef<string> SourceRef { get; }
}

public class ProducerActor : ReceiveActor
{
    private readonly string _data;

    public ProducerActor(string data)
    {
        _data = data;
        Receive<RequestStream>(_ =>
        {
            var sender = Sender;
            // create a source
            StreamLogs()
                // materialize it using stream refs
                .RunWith(StreamRefs.SourceRef<string>(), Context.System.Materializer())
                // and send to sender
                .PipeTo(sender, success: sourceRef => new EnvelopedStream(sourceRef));
        });

        Receive<string>(_ => Sender.Tell("pong"));
    }

    private Source<string, NotUsed> StreamLogs()
    {
        return Source.Single(_data);
    }

    public static Props Props(string data)
    {
        return Akka.Actor.Props.Create(() => new ProducerActor(data));
    }
}

public class ConsumerActor : ReceiveActor
{
    private readonly IMaterializer _materializer = Context.Materializer();

    public ConsumerActor(string sourceActorPath, IActorRef probe)
    {
        var sourceActor = Context.ActorSelection(sourceActorPath);

        Receive<StartListening>(_ => { sourceActor.Tell(new RequestStream(Self)); });

        Receive<EnvelopedStream>(offer =>
        {
            offer.SourceRef.Source.RunWith(Sink.ForEach<string>(s => { probe.Tell(s); }), _materializer);
        });
    }


    public static Props Props(string sourceActorPath, IActorRef probe)
    {
        return Akka.Actor.Props.Create(() => new ConsumerActor(sourceActorPath, probe));
    }
}

public class StreamRefsSerializerSpec : AkkaSpec
{
    private readonly TestProbe _probe;
    private readonly IActorRef _remoteActor;
    protected readonly ActorMaterializer Materializer;

    protected readonly ActorSystem RemoteSystem;

    public StreamRefsSerializerSpec(ITestOutputHelper output) : this(Config(), output)
    {
    }

    protected StreamRefsSerializerSpec(Config config, ITestOutputHelper output = null) : base(config, output)
    {
        Materializer = Sys.Materializer();
        RemoteSystem = ActorSystem.Create("remote-system-1", Config());
        InitializeLogger(RemoteSystem);
        _probe = CreateTestProbe();

        var it = RemoteSystem.ActorOf(DataSourceActor.Props(_probe.Ref), "remoteActor");
        var remoteAddress = ((ActorSystemImpl)RemoteSystem).Provider.DefaultAddress;
        Sys.ActorSelection(it.Path.ToStringWithAddress(remoteAddress)).Tell(new Identify("hi"));

        _remoteActor = ExpectMsg<ActorIdentity>(TimeSpan.FromMinutes(30)).Subject;
    }

    public static Config Config()
    {
        var address = TestUtils.TemporaryServerAddress();
        return ConfigurationFactory.ParseString($@"        
            akka {{
              loglevel = INFO
              actor {{
                provider = remote
                serialize-messages = off
              }}
              remote.dot-netty.tcp {{
                port = {address.Port}
                hostname = ""{address.Address}""
              }}
            }}")
            .WithFallback(ConfigurationFactory.Default());
    }

    protected override void BeforeTermination()
    {
        Materializer.Dispose();
        base.BeforeTermination();
    }

    protected override void AfterAll()
    {
        Shutdown(RemoteSystem);
        base.AfterAll();
    }

    [Fact]
    public void source_ref_must_be_correctly_sent_over_wire_even_if_enveloped_in_poco()
    {
        const string payload = "streamed data";

        var source = ActorOf(ProducerActor.Props(payload), "source");
        var remoteAddress = ((ActorSystemImpl)Sys).Provider.DefaultAddress;

        var sinkActor =
            RemoteSystem.ActorOf(ConsumerActor.Props(source.Path.ToStringWithAddress(remoteAddress), _probe), "sink");
        sinkActor.Tell(new StartListening());

        // when running in Azure DevOps, greater timeout needed to ensure real Remoting has enough time to handle stuff
        _probe.ExpectMsg(payload, TimeSpan.FromSeconds(30));
    }
}