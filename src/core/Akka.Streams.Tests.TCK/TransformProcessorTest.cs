//-----------------------------------------------------------------------
// <copyright file="TransformProcessorTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK
{
    class TransformProcessorTest : AkkaIdentityProcessorVerification<int?>
    {
        public override int? CreateElement(int element) => element;

        public override IProcessor<int?,int?> CreateIdentityProcessor(int bufferSize)
        {
            var settings = ActorMaterializerSettings.Create(System).WithInputBuffer(bufferSize/2, bufferSize);
            var materializer = ActorMaterializer.Create(System, settings);

            return Flow.Create<int?>().Transform(() => new Stage()).ToProcessor().Run(materializer);
        }

        private sealed class Stage : PushStage<int?, int?>
        {
            public override ISyncDirective OnPush(int? element, IContext<int?> context) => context.Push(element);
        }
    }
}
