// <copyright file="FlowZipWithIndexSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2017 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowZipWithIndexSpec : AkkaSpec
    {
        public FlowZipWithIndexSpec()
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer( 2, 16);
            Materializer = Sys.Materializer(settings);
        }

        private ActorMaterializer Materializer { get; }


        [Fact]
        public void A_ZipWithIndex_for_Flow_must_work_in_the_happy_case()
        {
            var probe = this.CreateManualSubscriberProbe<Tuple<int, long>>();
            Source.From(Enumerable.Range(7, 4)).ZipWithIndex().RunWith(Sink.FromSubscriber(probe), Materializer);

            var subscription = probe.ExpectSubscription();

            subscription.Request(2);
            probe.ExpectNext(Tuple.Create(7, 0L));
            probe.ExpectNext(Tuple.Create(8, 1L));

            subscription.Request(1);
            probe.ExpectNext(Tuple.Create(9, 2L));

            subscription.Request(1);
            probe.ExpectNext(Tuple.Create(10, 3L));

            probe.ExpectComplete();
        }
    }
}
