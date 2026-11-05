//-----------------------------------------------------------------------
// <copyright file="SetupFlowElementSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public sealed class SetupFlowElementSpec : AkkaSpec
    {
        private readonly ActorMaterializer _materializer;

        public SetupFlowElementSpec(ITestOutputHelper output) : base(output)
        {
            _materializer = Sys.Materializer();
        }

        [Theory(DisplayName = "Flow.Setup should forward every element through an identity flow")]
        [InlineData(1, false)]
        [InlineData(3, false)]
        [InlineData(3, true)]
        public async Task Should_forward_every_element_when_wrapping_an_identity_flow(int count, bool asyncBoundary)
        {
            var expected = Enumerable.Range(1, count).ToArray();
            var flow = Flow.Setup((_, _) => Flow.Create<int>());
            if (asyncBoundary)
                flow = flow.Async();

            var actual = await Source.From(expected)
                .Via(flow)
                .RunWith(Sink.Seq<int>(), _materializer)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.Equal(expected, actual);
        }

        [Fact(DisplayName = "Flow.Setup should preserve transformed elements and the inner materialized value")]
        public async Task Should_preserve_elements_and_materialized_value_when_wrapping_a_transform()
        {
            var expectedMaterialized = new object();
            var flow = Flow.Setup((_, _) => Flow.Create<string>()
                .Select(value => value.Length)
                .MapMaterializedValue(_ => expectedMaterialized));

            var (materialized, completion) = Source.From(new[] { "a", "bb", "ccc" })
                .ViaMaterialized(flow, Keep.Right)
                .ToMaterialized(Sink.Seq<int>(), Keep.Both)
                .Run(_materializer);

            Assert.Same(expectedMaterialized,
                await materialized.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal(new[] { 1, 2, 3 },
                await completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }

        [Fact(DisplayName = "Flow.Setup should deliver elements as demand arrives and propagate completion")]
        public async Task Should_deliver_elements_and_completion_when_demand_arrives_incrementally()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var (publisher, subscriber) = this.SourceProbe<int>()
                .Via(Flow.Setup((_, _) => Flow.Create<int>()))
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(_materializer);

            for (var value = 1; value <= 3; value++)
            {
                await subscriber.RequestAsync(1);
                await publisher.SendNextAsync(value, cancellationToken);
                await subscriber.ExpectNextAsync(value, TimeSpan.FromSeconds(5), cancellationToken);
            }

            await publisher.SendCompleteAsync(cancellationToken);
            await subscriber.ExpectCompleteAsync(cancellationToken: cancellationToken);
        }
    }
}
