//-----------------------------------------------------------------------
// <copyright file="EmptyPublisherTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Implementation;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK
{
    class EmptyPublisherTest : AkkaPublisherVerification<int>
    {
        public override IPublisher<int> CreatePublisher(long elements) => EmptyPublisher<int>.Instance;

        public override long MaxElementsFromPublisher { get; } = 0;
    }
}
