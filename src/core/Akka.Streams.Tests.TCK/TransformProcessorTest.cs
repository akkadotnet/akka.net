//-----------------------------------------------------------------------
// <copyright file="TransformProcessorTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

#pragma warning disable CS0618 // Type or member is obsolete
            return Flow.Create<int?>().Transform(() => new Stage()).ToProcessor().Run(materializer);
#pragma warning restore CS0618 // Type or member is obsolete
        }

#pragma warning disable CS0618 // Type or member is obsolete
        private sealed class Stage : PushStage<int?, int?>
#pragma warning restore CS0618 // Type or member is obsolete
        {
            public override ISyncDirective OnPush(int? element, IContext<int?> context) => context.Push(element);
        }
    }
}
