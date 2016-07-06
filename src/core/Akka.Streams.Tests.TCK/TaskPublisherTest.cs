//-----------------------------------------------------------------------
// <copyright file="TaskPublisherTest.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK
{
    // JVM : FuturePublisherTest
    class TaskPublisherTest : AkkaPublisherVerification<int>
    {
        public override IPublisher<int> CreatePublisher(long elements)
        {
            var completion = new TaskCompletionSource<int>();
            var publisher = Source.FromTask(completion.Task).RunWith(Sink.AsPublisher<int>(false), Materializer);
            completion.SetResult(0);
            return publisher;
        }

        public override long MaxElementsFromPublisher { get; } = 1;
    }
}
