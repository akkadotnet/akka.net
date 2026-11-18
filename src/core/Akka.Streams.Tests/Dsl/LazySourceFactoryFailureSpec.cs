//-----------------------------------------------------------------------
// <copyright file="LazySourceFactoryFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public sealed class LazySourceFactoryFailureSpec : AkkaSpec
    {
        private readonly ActorMaterializer _materializer;

        public LazySourceFactoryFailureSpec(ITestOutputHelper output) : base(output)
        {
            _materializer = Sys.Materializer();
        }

        [Theory(DisplayName = "A lazy source should preserve creation failures in both materialized and downstream tasks")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_preserve_original_failure_when_factory_or_inner_materialization_throws(
            bool duringMaterialization)
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var failure = new InvalidOperationException("lazy source creation failed");
                var (materialized, completion) = Source.Lazily<int, NotUsed>(() =>
                    {
                        if (!duringMaterialization)
                            throw failure;

                        return Source.Single(1).MapMaterializedValue<NotUsed>(_ => throw failure);
                    })
                    .ToMaterialized(Sink.Ignore<int>(), Keep.Both)
                    .Run(_materializer);

                // Observe both failures before asserting.
                var materializedFailure = await Record.ExceptionAsync(() =>
                    materialized.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
                var streamFailure = await Record.ExceptionAsync(() =>
                    completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

                Assert.Same(failure, materializedFailure);
                Assert.Same(failure, streamFailure);
            }, _materializer, cancellationToken: TestContext.Current.CancellationToken);
        }
    }
}
