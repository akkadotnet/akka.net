//-----------------------------------------------------------------------
// <copyright file="FlattenTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK
{
    class FlattenTest : AkkaPublisherVerification<int>
    {
        public override IPublisher<int> CreatePublisher(long elements)
        {
            var s1 = Source.From(Enumerate(elements/2));
            var s2 = Source.From(Enumerate((elements + 1)/2));
            return Source.From(new[] {s1, s2})
                .ConcatMany(x => x)
                .RunWith(Sink.AsPublisher<int>(false), Materializer);
        }
    }
}
