//-----------------------------------------------------------------------
// <copyright file="GroupByTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK
{
    class GroupByTest : AkkaPublisherVerification<int>
    {
        public override IPublisher<int> CreatePublisher(long elements)
        {
            if (elements == 0)
                return EmptyPublisher<int>.Instance;

            var flow = (Source<Source<int, NotUsed>, NotUsed>)
                Source.From(Enumerate(elements))
                    .GroupBy(1, elem => "all")
                    .PrefixAndTail(0)
                    .Select(tuple => tuple.Item2)
                    .ConcatSubstream();

            var futureGroupSource = flow.RunWith(Sink.First<Source<int, NotUsed>>(), Materializer);
            var groupSource = futureGroupSource.Result;

            return groupSource.RunWith(Sink.AsPublisher<int>(false), Materializer);
        }
    }
}
