﻿// -----------------------------------------------------------------------
//  <copyright file="SingleElementSourceTest.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK;

internal class SingleElementSourceTest : AkkaPublisherVerification<int>
{
    public override long MaxElementsFromPublisher { get; } = 1;

    public override IPublisher<int> CreatePublisher(long elements)
    {
        return Source.Single(1).RunWith(Sink.AsPublisher<int>(false), Materializer);
    }
}