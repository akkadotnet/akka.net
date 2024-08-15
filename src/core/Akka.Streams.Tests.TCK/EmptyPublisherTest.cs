// -----------------------------------------------------------------------
//  <copyright file="EmptyPublisherTest.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Streams.Implementation;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK;

internal class EmptyPublisherTest : AkkaPublisherVerification<int>
{
    public override long MaxElementsFromPublisher { get; } = 0;

    public override IPublisher<int> CreatePublisher(long elements)
    {
        return EmptyPublisher<int>.Instance;
    }
}