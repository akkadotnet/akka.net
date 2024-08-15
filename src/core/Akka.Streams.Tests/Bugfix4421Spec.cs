// -----------------------------------------------------------------------
//  <copyright file="Bugfix4421Spec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests;

public class Bugfix4421Spec : AkkaSpec
{
    public Bugfix4421Spec(ITestOutputHelper helper) : base(Config, helper)
    {
    }

    private static Config Config => ConfigurationFactory.ParseString(@"
            akka {
              actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""

              remote {
                dot-netty.tcp {
                    port = 8080
                    hostname = localhost
                }
              }
            }");

    [Fact]
    public async Task SinkRef_declared_inside_poco_must_be_serialized_properly()
    {
        var actor = Sys.ActorOf(Props.Create<DataSource>());

        var sink = Sink.ForEach<string>(str => { Output.WriteLine(str); });

        var sinkRef = await StreamRefs.SinkRef<string>()
            .Throttle(1, TimeSpan.FromMilliseconds(100), 1, ThrottleMode.Shaping)
            .To(sink)
            .Run(Sys.Materializer());

        var message = new MeasurementsSinkReady(sinkRef);

        var serializer = Sys.Serialization.FindSerializerFor(message);

        byte[] serialized = null;
        serializer.Invoking(s => serialized = s.ToBinary(message)).Should().NotThrow();
        object deserialized = null;
        serializer.Invoking(s => deserialized = s.FromBinary<MeasurementsSinkReady>(serialized)).Should().NotThrow();
        deserialized.Should().BeOfType<MeasurementsSinkReady>();
    }

    private class MeasurementsSinkReady
    {
        public MeasurementsSinkReady(ISinkRef<string> sinkRef)
        {
            SinkRef = sinkRef;
        }

        public ISinkRef<string> SinkRef { get; }
    }

    private class DataSource : ReceiveActor
    {
        public DataSource()
        {
            Receive<MeasurementsSinkReady>(request =>
            {
                Source.From(Enumerable.Range(1, 100))
                    .Select(i => i.ToString())
                    .RunWith(request.SinkRef.Sink, Context.System.Materializer());
            });
        }

        public static IActorRef DataReceiverActorRef { get; set; }
    }
}