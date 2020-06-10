//-----------------------------------------------------------------------
// <copyright file="StreamRefsSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Event;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;

namespace Akka.Streams.Tests
{
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
        public IActorRef ActorRef { get; }

        public RequestStream(IActorRef actorRef)
        {
            ActorRef = actorRef;
        }
    }

    [Serializable]
    public sealed class EnvelopedStream
    {
        public ISourceRef<string> SourceRef { get; }

        public EnvelopedStream(ISourceRef<string> sourceRef)
        {
            SourceRef = sourceRef;
        }
    }

    public class ProducerActor : ReceiveActor
    {
        public ProducerActor(string data)
        {
            _data = data;
            Receive<RequestStream>(request =>
            {
                // create a source
                StreamLogs()
                    // materialize it using stream refs
                    .RunWith(StreamRefs.SourceRef<string>(), Context.System.Materializer())
                    // and send to sender
                    .PipeTo(Sender, success: sourceRef => new EnvelopedStream(sourceRef));
            });

            Receive<string>(_ => Sender.Tell("pong"));
        }

        private readonly string _data;

        private Source<string, NotUsed> StreamLogs() => Source.Single(_data);

        public static Props Props(string data) => Akka.Actor.Props.Create(() => new ProducerActor(data));
    }

    public class ConsumerActor : ReceiveActor
    {
        private IMaterializer _materializer = Context.Materializer();

        public ConsumerActor(string sourceActorPath, IActorRef probe)
        {
            var sourceActor = Context.ActorSelection(sourceActorPath);

            Receive<StartListening>((listening =>
            {
                sourceActor.Tell(new RequestStream(Self));
            }));

            Receive<EnvelopedStream>(offer =>
            {
                offer.SourceRef.Source.RunWith(Sink.ForEach<string>(s =>
                {
                    probe.Tell(s);
                }), _materializer);
            });
        }
      

        public static Props Props(string sourceActorPath, IActorRef probe)
        {
            return Akka.Actor.Props.Create(() => new ConsumerActor(sourceActorPath, probe));
        }
    }

    public class StreamRefsSerializerSpec : AkkaSpec
    {
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

        public StreamRefsSerializerSpec(ITestOutputHelper output) : this(Config(), output: output)
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

        protected readonly ActorSystem RemoteSystem;
        protected readonly ActorMaterializer Materializer;
        private readonly TestProbe _probe;
        private readonly IActorRef _remoteActor;

        protected override void BeforeTermination()
        {
            base.BeforeTermination();
            RemoteSystem.Dispose();
            Materializer.Dispose();
        }

        [Fact]
        public void source_ref_must_be_correctly_sent_over_wire_even_if_enveloped_in_poco()
        {
            const string payload = "streamed data";

            var source = ActorOf(ProducerActor.Props(payload), "source");
            var remoteAddress = ((ActorSystemImpl)Sys).Provider.DefaultAddress;

            var sinkActor = RemoteSystem.ActorOf(ConsumerActor.Props(source.Path.ToStringWithAddress(remoteAddress), _probe), "sink");
            sinkActor.Tell(new StartListening());

            _probe.ExpectMsg(payload);
        }
    }
}
