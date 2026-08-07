//-----------------------------------------------------------------------
// <copyright file="AsyncEnumerableDocTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit;
using Xunit;

namespace DocsExamples.Streams
{
    public class AsyncEnumerableDocTests : TestKit
    {
        #region source-from-asyncenumerable
        private static async IAsyncEnumerable<int> TickAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 1; i <= 3; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(10, cancellationToken);
                yield return i;
            }
        }

        public async Task SourceFromAsyncEnumerable()
        {
            var materializer = Sys.Materializer();

            // Factory is invoked for every materialization / subscriber
            await Source.From(() => TickAsync())
                .RunForeach(Console.WriteLine, materializer);
        }
        #endregion

        #region run-as-asyncenumerable
        public async Task RunSourceAsAsyncEnumerable()
        {
            var materializer = Sys.Materializer();

            var source = Source.From(new[] { 1, 2, 3 })
                .Select(x => x * 2);

            // Re-runnable: each await foreach materializes the stream again
            await foreach (var n in source.RunAsAsyncEnumerable(materializer))
            {
                Console.WriteLine(n);
            }
        }
        #endregion

        #region run-as-asyncenumerable-buffer
        public async Task RunSourceAsAsyncEnumerableWithCustomBuffer()
        {
            var materializer = Sys.Materializer();

            var source = Source.From(Enumerable.Range(1, 20));

            await foreach (var n in source.RunAsAsyncEnumerableBuffer(materializer, minBuffer: 2, maxBuffer: 8))
            {
                Console.WriteLine(n);
            }
        }
        #endregion

        [Fact]
        public async Task Samples_should_compile_and_run()
        {
            await SourceFromAsyncEnumerable();
            await RunSourceAsAsyncEnumerable();
            await RunSourceAsAsyncEnumerableWithCustomBuffer();
        }
    }
}
