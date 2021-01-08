//-----------------------------------------------------------------------
// <copyright file="HubsDocTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit2;
using Xunit;
using Xunit.Abstractions;
using Akka.Actor;

namespace DocsExamples.Streams
{
    public class HubsDocTests : TestKit
    {
        private ActorMaterializer Materializer { get; }

        public HubsDocTests(ITestOutputHelper output) 
            : base("{}", output)
        {
            Materializer = Sys.Materializer();
        }

        [Fact]
        public void Hubs_must_demonstrate_creating_a_dynamic_merge()
        {
            void WriteLine(string s) => TestActor.Tell(s);

            #region merge-hub
            // A simple consumer that will print to the console for now
            Sink<string, Task> consumer = Sink.ForEach<string>(WriteLine);

            // Attach a MergeHub Source to the consumer. This will materialize to a
            // corresponding Sink.
            IRunnableGraph<Sink<string, NotUsed>> runnableGraph =
                MergeHub.Source<string>(perProducerBufferSize: 16).To(consumer);

            // By running/materializing the consumer we get back a Sink, and hence
            // now have access to feed elements into it. This Sink can be materialized
            // any number of times, and every element that enters the Sink will
            // be consumed by our consumer.
            Sink<string, NotUsed> toConsumer = runnableGraph.Run(Materializer);

            // Feeding two independent sources into the hub.
            Source.Single("Hello!").RunWith(toConsumer, Materializer);
            Source.Single("Hub!").RunWith(toConsumer, Materializer);
            #endregion

            ExpectMsgAllOf("Hello!", "Hub!");
        }

        [Fact]
        public void Hubs_must_demonstrate_creating_a_dynamic_broadcast()
        {
            #region broadcast-hub
            Source<string, ICancelable> producer = Source.Tick(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), "New message");

            // Attach a BroadcastHub Sink to the producer. This will materialize to a
            // corresponding Source.
            // (We need to use ToMaterialized and Keep.Right since by default the materialized
            // value to the left is used)
            IRunnableGraph<Source<string, NotUsed>> runnableGraph =
                producer.ToMaterialized(BroadcastHub.Sink<string>(bufferSize: 256), Keep.Right);

            // By running/materializing the producer, we get back a Source, which
            // gives us access to the elements published by the producer.
            Source<string, NotUsed> fromProducer = runnableGraph.Run(Materializer);

            // Print out messages from the producer in two independent consumers
            fromProducer.RunForeach(msg => Console.WriteLine($"consumer1:{msg}"), Materializer);
            fromProducer.RunForeach(msg => Console.WriteLine($"consumer2:{msg}"), Materializer);
            #endregion
        }

        [Fact]
        public void Hubs_must_demonstrate_combination()
        {
            void WriteLine(string s) => TestActor.Tell(s);

            #region pub-sub-1
            // Obtain a Sink and Source which will publish and receive from the "bus" respectively.
            var (sink, source) = MergeHub
                .Source<string>(perProducerBufferSize: 16)
                .ToMaterialized(BroadcastHub.Sink<string>(bufferSize: 256), Keep.Both)
                .Run(Materializer);
            #endregion

            #region pub-sub-2
            // Ensure that the Broadcast output is dropped if there are no listening parties.
            // If this dropping Sink is not attached, then the broadcast hub will not drop any
            // elements itself when there are no subscribers, backpressuring the producer instead.
            source.RunWith(Sink.Ignore<string>(), Materializer);
            #endregion

            #region pub-sub-3
            // We create now a Flow that represents a publish-subscribe channel using the above
            // started stream as its "topic". We add two more features, external cancellation of
            // the registration and automatic cleanup for very slow subscribers.
            Flow<string, string, UniqueKillSwitch> busFlow = Flow.FromSinkAndSource(sink, source)
                .JoinMaterialized(KillSwitches.SingleBidi<string, string>(), Keep.Right)
                .BackpressureTimeout(TimeSpan.FromSeconds(3));
            #endregion

            #region pub-sub-4

            UniqueKillSwitch killSwitch = Source
                .Repeat("Hello world!")
                .ViaMaterialized(busFlow, Keep.Right)
                .To(Sink.ForEach<string>(WriteLine))
                .Run(Materializer);

            // Shut down externally
            killSwitch.Shutdown();
            #endregion
        }

        [Fact]
        public void Hubs_must_demonstrate_creating_a_dynamic_partition_hub()
        {
            #region partition-hub

            // A simple producer that publishes a new "message-" every second
            Source<string, NotUsed> producer = Source.Tick(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), "message")
                .MapMaterializedValue(_ => NotUsed.Instance)
                .ZipWith(Source.From(Enumerable.Range(1, 100)), (msg, i) => $"{msg}-{i}");

            // Attach a PartitionHub Sink to the producer. This will materialize to a
            // corresponding Source.
            // (We need to use toMat and Keep.right since by default the materialized
            // value to the left is used)
            IRunnableGraph<Source<string, NotUsed>> runnableGraph =
                producer.ToMaterialized(PartitionHub.Sink<string>(
                    (size, element) => Math.Abs(element.GetHashCode()) % size,
                    startAfterNrOfConsumers: 2, bufferSize: 256), Keep.Right);

            // By running/materializing the producer, we get back a Source, which
            // gives us access to the elements published by the producer.
            Source<string, NotUsed> fromProducer = runnableGraph.Run(Materializer);

            // Print out messages from the producer in two independent consumers
            fromProducer.RunForeach(msg => Console.WriteLine("Consumer1: " + msg), Materializer);
            fromProducer.RunForeach(msg => Console.WriteLine("Consumer2: " + msg), Materializer);

            #endregion
        }

        [Fact]
        public void Hubs_must_demonstrate_creating_a_dynamic_steful_partition_hub()
        {
            #region partition-hub-stateful

            // A simple producer that publishes a new "message-" every second
            Source<string, NotUsed> producer = Source.Tick(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), "message")
                .MapMaterializedValue(_ => NotUsed.Instance)
                .ZipWith(Source.From(Enumerable.Range(1, 100)), (msg, i) => $"{msg}-{i}");

            // New instance of the partitioner function and its state is created
            // for each materialization of the PartitionHub.
            Func<PartitionHub.IConsumerInfo, string, long> RoundRobbin()
            {
                var i = -1L;
                return (info, element) =>
                {
                    i++;
                    return info.ConsumerByIndex((int) (i % info.Size));
                };
            }

            // Attach a PartitionHub Sink to the producer. This will materialize to a
            // corresponding Source.
            // (We need to use toMat and Keep.right since by default the materialized
            // value to the left is used)
            IRunnableGraph<Source<string, NotUsed>> runnableGraph =
                producer.ToMaterialized(PartitionHub.StatefulSink(RoundRobbin,
                    startAfterNrOfConsumers: 2, bufferSize: 256), Keep.Right);

            // By running/materializing the producer, we get back a Source, which
            // gives us access to the elements published by the producer.
            Source<string, NotUsed> fromProducer = runnableGraph.Run(Materializer);

            // Print out messages from the producer in two independent consumers
            fromProducer.RunForeach(msg => Console.WriteLine("Consumer1: " + msg), Materializer);
            fromProducer.RunForeach(msg => Console.WriteLine("Consumer2: " + msg), Materializer);

            #endregion
        }

        [Fact]
        public void Hubs_must_demonstrate_creating_a_dynamic_partition_hub_routing_to_fastest_consumer()
        {
            #region partition-hub-fastest

            // A simple producer that publishes a new "message-" every second
            Source<int, NotUsed> producer = Source.From(Enumerable.Range(0, 100));

            // Attach a PartitionHub Sink to the producer. This will materialize to a
            // corresponding Source.
            // (We need to use toMat and Keep.right since by default the materialized
            // value to the left is used)
            IRunnableGraph<Source<int, NotUsed>> runnableGraph =
                producer.ToMaterialized(PartitionHub.StatefulSink<int>(
                    () => ((info, element) => info.ConsumerIds.Min(info.QueueSize)),
                    startAfterNrOfConsumers: 2, bufferSize: 256), Keep.Right);

            // By running/materializing the producer, we get back a Source, which
            // gives us access to the elements published by the producer.
            Source<int, NotUsed> fromProducer = runnableGraph.Run(Materializer);

            // Print out messages from the producer in two independent consumers
            fromProducer.RunForeach(msg => Console.WriteLine("Consumer1: " + msg), Materializer);
            fromProducer.Throttle(10, TimeSpan.FromMilliseconds(100), 10, ThrottleMode.Shaping)
                .RunForeach(msg => Console.WriteLine("Consumer2: " + msg), Materializer);

            #endregion
        }
    }
}
