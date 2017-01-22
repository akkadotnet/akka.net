﻿//-----------------------------------------------------------------------
// <copyright file="ForeachSinkSubscriberTest.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK
{
    class ForeachSinkSubscriberTest : AkkaSubscriberBlackboxVerification<int?>
    {
        public override int? CreateElement(int element) => element;

        public override ISubscriber<int?> CreateSubscriber() =>
            Flow.Create<int?>()
                .To(Sink.ForEach<int?>(_ => { }))
                .RunWith(Source.AsSubscriber<int?>(), Materializer);
    }
}
