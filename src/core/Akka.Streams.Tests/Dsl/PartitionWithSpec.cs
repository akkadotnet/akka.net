//-----------------------------------------------------------------------
// <copyright file="PartitionWithSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Util;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class PartitionWithSpec : Akka.TestKit.Xunit2.TestKit
    {
        private Flow<int, int, NotUsed> _flow = Flow.FromGraph(GraphDsl.Create(b =>
        {
            Either<int, int> Partition(int i)
            {
                if (i % 2 == 0)
                    return Either.Left(i / 2);

                return Either.Right(i * 3 - 1);
            }

            var pw = b.Add(new PartitionWith<int, int, int>(Partition));
            var mrg = b.Add(new Merge<int>(2));

            b.From(pw.Out0).To(mrg.In(0));
            b.From(pw.Out1).To(mrg.In(1));

            return new FlowShape<int, int>(pw.In, mrg.Out);
        }));

        [Fact]
        public void PartitionWith_should_partition_ints_according_to_their_parity()
        {
            var t = this.SourceProbe<int>()
                .Via(_flow)
                .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                .Run(Sys.Materializer());

            var source = t.Item1;
            var sink = t.Item2;

            sink.Request(99);
            source.SendNext(1);
            source.SendNext(2);
            source.SendNext(3);
            sink.ExpectNext(2, 1, 8);
            source.SendComplete();
            sink.ExpectComplete();
        }

        [Fact]
        public void PartitionWith_should_not_emit_any_value_for_an_empty_source()
        {
            Source.Empty<int>()
                .Via(_flow)
                .RunWith(this.SinkProbe<int>(), Sys.Materializer())
                .Request(99)
                .ExpectComplete();
        }

        [Fact]
        public void PartitionWith_should_fail_on_upstream_failure()
        {
            var t = this.SourceProbe<int>()
                   .Via(_flow)
                   .ToMaterialized(this.SinkProbe<int>(), Keep.Both)
                   .Run(Sys.Materializer());

            var source = t.Item1;
            var sink = t.Item2;

            sink.Request(99);
            source.SendError(new Exception());
            sink.ExpectError();
        }
    }
}
