//-----------------------------------------------------------------------
// <copyright file="MaybeSourceTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK
{
    class MaybeSourceTest : AkkaPublisherVerification<int>
    {
        public override IPublisher<int> CreatePublisher(long elements)
        {
            var tuple = Source.Maybe<int>().ToMaterialized(Sink.AsPublisher<int>(false), Keep.Both).Run(Materializer);
            tuple.Item1.SetResult(1);
            return tuple.Item2;
        }

        public override long MaxElementsFromPublisher { get; } = 1;
    }
}
