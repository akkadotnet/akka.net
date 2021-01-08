//-----------------------------------------------------------------------
// <copyright file="FusableProcessorTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Fusing;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK
{
    class FusableProcessorTest : AkkaIdentityProcessorVerification<int?>
    {
        public override int? CreateElement(int element) => element;

        public override IProcessor<int?,int?> CreateIdentityProcessor(int bufferSize)
        {
            var settings = ActorMaterializerSettings.Create(System).WithInputBuffer(bufferSize/2, bufferSize);
            var materializer = ActorMaterializer.Create(System, settings);

            // withAttributes "wraps" the underlying identity and protects it from automatic removal
            return Flow.Create<int?>()
                .Via(GraphStages.Identity<int?>())
                .Named("identity")
                .ToProcessor()
                .Run(materializer);
        }
    }
}
