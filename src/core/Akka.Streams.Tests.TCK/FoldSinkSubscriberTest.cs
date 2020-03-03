//-----------------------------------------------------------------------
// <copyright file="FoldSinkSubscriberTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK
{
    class FoldSinkSubscriberTest : AkkaSubscriberBlackboxVerification<int?>
    {
        public override int? CreateElement(int element) => element;

        public override ISubscriber<int?> CreateSubscriber() =>
            Flow.Create<int?>()
                .To(Sink.Aggregate<int?, int?>(0, (agg, i) => agg + i))
                .RunWith(Source.AsSubscriber<int?>(), Materializer);
    }
}
