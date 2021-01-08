//-----------------------------------------------------------------------
// <copyright file="SingleElementSourceTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK
{
    class SingleElementSourceTest : AkkaPublisherVerification<int>
    {
        public override IPublisher<int> CreatePublisher(long elements)
            => Source.Single(1).RunWith(Sink.AsPublisher<int>(false), Materializer);

        public override long MaxElementsFromPublisher { get; } = 1;
    }
}
