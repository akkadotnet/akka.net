//-----------------------------------------------------------------------
// <copyright file="SelectTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK
{
    class SelectTest : AkkaIdentityProcessorVerification<int?>
    {
        public override int? CreateElement(int element) => element;

        public override IProcessor<int?,int?> CreateIdentityProcessor(int bufferSize)
        {
            var materializer = ActorMaterializer.Create(System);
            return Flow.Create<int?>().Select(x => x).Named("identity").ToProcessor().Run(materializer);
        }
    }
}
