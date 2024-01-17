//-----------------------------------------------------------------------
// <copyright file="FlowDetacherSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowDetacherSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowDetacherSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public async Task A_Detacher_must_pass_through_all_elements()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var task = Source.From(Enumerable.Range(1, 100))                                                                             
                .Detach()                                                                             
                .RunWith(Sink.Seq<int>(), Materializer);
                (await task.WaitAsync(RemainingOrDefault)).Should().BeEquivalentTo(Enumerable.Range(1, 100));
            }, Materializer);
        }

        [Fact]
        public async Task A_Detacher_must_pass_through_failure()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var ex = new TestException("buh");
                var result = Source.From(Enumerable.Range(1, 100)).Select(x =>
                {
                    if (x == 50)
                        throw ex;
                    return x;
                }).Detach().RunWith(Sink.Seq<int>(), Materializer);

                (await Awaiting(() => result.WaitAsync(2.Seconds())).Should().ThrowAsync<TestException>())
                    .And.Should().Be(ex);
            }, Materializer);
        }

        [Fact]
        public async Task A_Detacher_must_emit_the_last_element_when_completed_Without_demand()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var probe = Source.Single(42).Detach().RunWith(this.SinkProbe<int>(), Materializer).EnsureSubscription();
                await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));
                await probe.RequestNextAsync(42);
            }, Materializer);
        }
    }
}
