﻿// -----------------------------------------------------------------------
//  <copyright file="MaybeSourceTest.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK;

internal class MaybeSourceTest : AkkaPublisherVerification<int>
{
    public override long MaxElementsFromPublisher { get; } = 1;

    public override IPublisher<int> CreatePublisher(long elements)
    {
        var tuple = Source.Maybe<int>().ToMaterialized(Sink.AsPublisher<int>(false), Keep.Both).Run(Materializer);
        tuple.Item1.SetResult(1);
        return tuple.Item2;
    }
}