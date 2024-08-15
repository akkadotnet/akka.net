// -----------------------------------------------------------------------
//  <copyright file="VirtualPublisherTest.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK;

internal class VirtualProcessorTest : AkkaIdentityProcessorVerification<int?>
{
    public override int? CreateElement(int element)
    {
        return element;
    }

    public override IProcessor<int?, int?> CreateIdentityProcessor(int bufferSize)
    {
        var materializer = ActorMaterializer.Create(System);
        var identity = Flow.Create<int?>().Select(x => x).Named("identity").ToProcessor().Run(materializer);
        var left = new VirtualProcessor<int?>();
        var right = new VirtualProcessor<int?>();
        left.Subscribe(identity);
        identity.Subscribe(right);
        return ProcessorFromSubscriberAndPublisher(left, right);
    }
}

internal class VirtualProcessorSingleTest : AkkaIdentityProcessorVerification<int?>
{
    public override int? CreateElement(int element)
    {
        return element;
    }

    public override IProcessor<int?, int?> CreateIdentityProcessor(int bufferSize)
    {
        return new VirtualProcessor<int?>();
    }
}