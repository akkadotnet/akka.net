// -----------------------------------------------------------------------
//  <copyright file="ForeachSinkSubscriberTest.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK;

internal class ForeachSinkSubscriberTest : AkkaSubscriberBlackboxVerification<int?>
{
    public override int? CreateElement(int element)
    {
        return element;
    }

    public override ISubscriber<int?> CreateSubscriber()
    {
        return Flow.Create<int?>()
            .To(Sink.ForEach<int?>(_ => { }))
            .RunWith(Source.AsSubscriber<int?>(), Materializer);
    }
}