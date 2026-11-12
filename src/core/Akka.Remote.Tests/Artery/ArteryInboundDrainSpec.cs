// -----------------------------------------------------------------------
// <copyright file="ArteryInboundDrainSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Artery;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    public sealed class ArteryInboundDrainSpec : AkkaSpec
    {
        public ArteryInboundDrainSpec(ITestOutputHelper output) : base(output) { }

        [Theory(DisplayName = "Inbound consumer completion waits for accepted lane work on every graph termination path")]
        [InlineData("finish")]
        [InlineData("failure")]
        [InlineData("cancel")]
        public async Task Should_WaitForLaneConsumers_When_GraphStops(string termination)
        {
            var context = new AssociationRegistryInboundContext(new AssociationRegistry(),
                new UniqueAddress(new Address("akka", "local", "127.0.0.1", 1234), 1), (_, _) => { });
            context.CompleteHandshake(new UniqueAddress(new Address("akka", "remote", "127.0.0.1", 4321), 42));
            var materializer = ActorMaterializer.Create(Sys);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var gate = new object();
            var gateOpen = false;
            void OpenGate()
            {
                lock (gate)
                {
                    gateOpen = true;
                    Monitor.PulseAll(gate);
                }
            }
            var entered = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
            var healthyDone = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stopped = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            var delivered = new ConcurrentQueue<(string Path, int Value)>();
            const string blocked = "akka://local/user/recipient-0";
            const string healthy = "akka://local/user/recipient-1";
            const int count = 8;
            var stage = new ArteryInboundProcessingStage(65536, 65536, Sys.Serialization, Sys.Scheduler,
                inboundLanes: 2, inboundLaneBufferSize: count * 2, inboundContext: context,
                dispatchOrdinary: (message, _, recipient) =>
                {
                    if (recipient == blocked)
                    {
                        entered.TrySetResult(Done.Instance);
                        // Dispatch is synchronous. Hold only this background consumer, using a
                        // bounded test gate without a disposable handle that can race teardown.
                        lock (gate)
                            while (!gateOpen)
                                if (!Monitor.Wait(gate, TimeSpan.FromSeconds(15)))
                                    throw new TimeoutException("The lane test gate was not released.");
                    }
                    var value = (int)message;
                    delivered.Enqueue((recipient, value));
                    if (recipient == healthy && value == count - 1)
                        healthyDone.TrySetResult(Done.Instance);
                }, onLaneConsumersStopped: completion => stopped.TrySetResult(completion));
            var ((queue, killSwitch), graph) = Source.Queue<ReadOnlySequence<byte>>(32, OverflowStrategy.Backpressure)
                .Via(stage)
                .ViaMaterialized(KillSwitches.Single<IInboundEnvelope>(), Keep.Both)
                .ToMaterialized(Sink.Ignore<IInboundEnvelope>(), Keep.Both)
                .Run(materializer);
            try
            {
                await queue.OfferAsync(Preamble(ArteryStreamId.Ordinary));
                for (var value = 0; value < count; value++)
                    foreach (var recipient in new[] { blocked, healthy })
                    {
                        using var encoded = ArteryEnvelopeCodec.Encode(Sys.Serialization, 42, null, recipient, value);
                        await queue.OfferAsync(new ReadOnlySequence<byte>(encoded.WrittenSpan.ToArray()));
                    }
                await entered.Task.WaitAsync(deadline.Token);
                await healthyDone.Task.WaitAsync(deadline.Token);
                if (termination == "failure")
                {
                    var error = new IOException("Controlled inbound failure.");
                    queue.Fail(error);
                    (await Assert.ThrowsAsync<IOException>(() => graph.WaitAsync(deadline.Token))).Should().BeSameAs(error);
                }
                else
                {
                    if (termination == "cancel")
                        killSwitch.Shutdown();
                    else
                        queue.Complete();
                    await graph.WaitAsync(deadline.Token);
                }
                var consumers = await stopped.Task.WaitAsync(deadline.Token);
                consumers.IsCompleted.Should().BeFalse("graph termination is separate from queued lane dispatch");
                delivered.Where(item => item.Path == blocked).Should().BeEmpty();
                OpenGate();
                await consumers.WaitAsync(deadline.Token);
                foreach (var recipient in new[] { blocked, healthy })
                    delivered.Where(item => item.Path == recipient).Select(item => item.Value)
                        .Should().Equal(Enumerable.Range(0, count));
                delivered.Should().HaveCount(count * 2);
            }
            finally
            {
                OpenGate();
                killSwitch.Shutdown();
                queue.Complete();
                try
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await (await stopped.Task.WaitAsync(cleanup.Token)).WaitAsync(cleanup.Token);
                }
                finally
                {
                    materializer.Shutdown();
                }
            }
        }

        [Theory(DisplayName = "Inbound consumer completion also reports streams without lane consumers")]
        [InlineData(1, 2)]
        [InlineData(4, 1)]
        [InlineData(4, 3)]
        public async Task Should_ReportCompletion_When_StreamDoesNotUseLanes(int lanes, int role)
        {
            var stopped = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            var context = new AssociationRegistryInboundContext(new AssociationRegistry(),
                new UniqueAddress(new Address("akka", "local", "127.0.0.1", 1234), 1), (_, _) => { });
            var stage = new ArteryInboundProcessingStage(65536, 65536, Sys.Serialization, Sys.Scheduler,
                inboundLanes: lanes, inboundLaneBufferSize: 16, inboundContext: context,
                dispatchOrdinary: (_, _, _) => throw new InvalidOperationException("No lane dispatch expected."),
                onLaneConsumersStopped: completion => stopped.TrySetResult(completion));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var materializer = ActorMaterializer.Create(Sys);
            try
            {
                await Source.Single(Preamble((ArteryStreamId)role)).Via(stage)
                    .RunWith(Sink.Ignore<IInboundEnvelope>(), materializer).WaitAsync(deadline.Token);
                await (await stopped.Task.WaitAsync(deadline.Token)).WaitAsync(deadline.Token);
            }
            finally
            {
                materializer.Shutdown();
            }
        }

        private static ReadOnlySequence<byte> Preamble(ArteryStreamId role)
        {
            var bytes = new byte[ArteryConnectionHeader.Length];
            ArteryConnectionHeader.WriteTo(bytes, role);
            return new ReadOnlySequence<byte>(bytes);
        }
    }
}
