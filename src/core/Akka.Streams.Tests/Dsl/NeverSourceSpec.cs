//-----------------------------------------------------------------------
// <copyright file="NeverSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class NeverSourceSpec : AkkaSpec
    {
        private readonly IMaterializer materializer;

        public NeverSourceSpec() => materializer = ActorMaterializer.Create(Sys);

        [Fact]
        public async Task NeverSource_must_never_completes()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var neverSource = Source.Never<int>();
                var pubSink = Sink.AsPublisher<int>(false);

                var neverPub = neverSource.ToMaterialized(pubSink, Keep.Right).Run(materializer);

                var c = this.CreateManualSubscriberProbe<int>();
                neverPub.Subscribe(c);
                var subs = await c.ExpectSubscriptionAsync();
                subs.Request(1);
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

                subs.Cancel();
            }, materializer);
        }
    }
}
