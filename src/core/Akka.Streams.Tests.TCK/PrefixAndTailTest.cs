//-----------------------------------------------------------------------
// <copyright file="PrefixAndTailTest.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK
{
    class PrefixAndTailTest : AkkaPublisherVerification<int>
    {
        public override IPublisher<int> CreatePublisher(long elements)
        {
            var futureTailSource =
                Source.From(Enumerate(elements))
                    .PrefixAndTail(0)
                    .Select(tuple => tuple.Item2)
                    .RunWith(Sink.First<Source<int, NotUsed>>(), Materializer);
            var tailSource = futureTailSource.Result;
            return tailSource.RunWith(Sink.AsPublisher<int>(false), Materializer);
        }
    }
}
