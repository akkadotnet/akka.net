//-----------------------------------------------------------------------
// <copyright file="GraphUnzipSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class GraphUnzipSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public GraphUnzipSpec(ITestOutputHelper helper) : base (helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public async Task A_Unzip_must_unzip_to_two_subscribers()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<string>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var unzip = b.Add(new UnZip<int, string>());
                    var source =
                        Source.From(new[]
                        {
                            new KeyValuePair<int, string>(1, "a"),
                            new KeyValuePair<int, string>(2, "b"),
                            new KeyValuePair<int, string>(3, "c")
                        });

                    b.From(source).To(unzip.In);
                    b.From(unzip.Out0)
                        .Via(Flow.Create<int>().Buffer(16, OverflowStrategy.Backpressure).Select(x => x * 2))
                        .To(Sink.FromSubscriber(c1));
                    b.From(unzip.Out1)
                        .Via(Flow.Create<string>().Buffer(16, OverflowStrategy.Backpressure))
                        .To(Sink.FromSubscriber(c2));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var sub1 = await c1.ExpectSubscriptionAsync();
                var sub2 = await c2.ExpectSubscriptionAsync();

                sub1.Request(1);
                sub2.Request(2);
                await c1.ExpectNextAsync(1 * 2);
                await c1.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                c2.ExpectNext("a", "b");
                await c2.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                sub1.Request(3);
                c1.ExpectNext(2 * 2, 3 * 2);
                await c1.ExpectCompleteAsync();
                sub2.Request(3);
                await c2.ExpectNextAsync("c");
                await c2.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Unzip_must_produce_to_right_downstream_even_though_left_downstream_cancels()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<string>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var unzip = b.Add(new UnZip<int, string>());
                    var source =
                        Source.From(new[]
                        {
                            new KeyValuePair<int, string>(1, "a"),
                            new KeyValuePair<int, string>(2, "b"),
                            new KeyValuePair<int, string>(3, "c")
                        });

                    b.From(source).To(unzip.In);
                    b.From(unzip.Out0).To(Sink.FromSubscriber(c1));
                    b.From(unzip.Out1).To(Sink.FromSubscriber(c2));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var sub1 = await c1.ExpectSubscriptionAsync();
                var sub2 = await c2.ExpectSubscriptionAsync();

                sub1.Cancel();
                sub2.Request(3);
                c2.ExpectNext("a", "b", "c");
                await c2.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Unzip_must_produce_to_left_downstream_even_though_right_downstream_cancels()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<string>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var unzip = b.Add(new UnZip<int, string>());
                    var source =
                        Source.From(new[]
                        {
                            new KeyValuePair<int, string>(1, "a"),
                            new KeyValuePair<int, string>(2, "b"),
                            new KeyValuePair<int, string>(3, "c")
                        });

                    b.From(source).To(unzip.In);
                    b.From(unzip.Out0).To(Sink.FromSubscriber(c1));
                    b.From(unzip.Out1).To(Sink.FromSubscriber(c2));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var sub1 = await c1.ExpectSubscriptionAsync();
                var sub2 = await c2.ExpectSubscriptionAsync();

                sub2.Cancel();
                sub1.Request(3);
                c1.ExpectNext(1, 2, 3);
                await c1.ExpectCompleteAsync();

            }, Materializer);
        }


        [Fact]
        public async Task A_Unzip_must_not_push_twice_when_pull_is_followed_by_cancel_before_element_has_been_pushed()
        {
            var c1 = this.CreateManualSubscriberProbe<int>();
            var c2 = this.CreateManualSubscriberProbe<string>();

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var unzip = b.Add(new UnZip<int, string>());
                var source = Source.From(new[]
                {
                    new KeyValuePair<int, string>(1, "a"),
                    new KeyValuePair<int, string>(2, "b"),
                    new KeyValuePair<int, string>(3, "c")
                });

                b.From(source).To(unzip.In);
                b.From(unzip.Out0).To(Sink.FromSubscriber(c1));
                b.From(unzip.Out1).To(Sink.FromSubscriber(c2));
                return ClosedShape.Instance;
            })).Run(Materializer);

            var sub1 = await c1.ExpectSubscriptionAsync();
            var sub2 = await c2.ExpectSubscriptionAsync();
            sub2.Request(3);
            sub1.Request(3);
            sub2.Cancel();
            c1.ExpectNext( 1,2,3);
            await c1.ExpectCompleteAsync();
        }

        [Fact]
        public async Task A_Unzip_must_not_loose_elements_when_pull_is_followed_by_cancel_before_other_sink_has_requested()
        {
            var c1 = this.CreateManualSubscriberProbe<int>();
            var c2 = this.CreateManualSubscriberProbe<string>();

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var unzip = b.Add(new UnZip<int, string>());
                var source = Source.From(new[]
                {
                    new KeyValuePair<int, string>(1, "a"),
                    new KeyValuePair<int, string>(2, "b"),
                    new KeyValuePair<int, string>(3, "c")
                });

                b.From(source).To(unzip.In);
                b.From(unzip.Out0).To(Sink.FromSubscriber(c1));
                b.From(unzip.Out1).To(Sink.FromSubscriber(c2));
                return ClosedShape.Instance;
            })).Run(Materializer);

            var sub1 = await c1.ExpectSubscriptionAsync();
            var sub2 = await c2.ExpectSubscriptionAsync();
            sub2.Request(3);
            sub2.Cancel();
            sub1.Request(3);
            c1.ExpectNext(1, 2, 3);
            await c1.ExpectCompleteAsync();
        }

        [Fact]
        public async Task A_Unzip_must_cancel_upstream_when_downstream_cancel()
        {
            await this.AssertAllStagesStoppedAsync(async () => {
                var p1 = this.CreateManualPublisherProbe<KeyValuePair<int, string>>();
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<string>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var unzip = b.Add(new UnZip<int, string>());
                    var source = Source.FromPublisher(p1.Publisher);

                    b.From(source).To(unzip.In);
                    b.From(unzip.Out0).To(Sink.FromSubscriber(c1));
                    b.From(unzip.Out1).To(Sink.FromSubscriber(c2));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var p1Sub = await p1.ExpectSubscriptionAsync();
                var sub1 = await c1.ExpectSubscriptionAsync();
                var sub2 = await c2.ExpectSubscriptionAsync();

                sub1.Request(3);
                sub2.Request(3);
                await p1.ExpectRequestAsync(p1Sub, 16);
                p1Sub.SendNext(new KeyValuePair<int, string>(1, "a"));
                await c1.ExpectNextAsync(1);
                await c2.ExpectNextAsync("a");
                p1Sub.SendNext(new KeyValuePair<int, string>(2, "b"));
                await c1.ExpectNextAsync(2);
                await c2.ExpectNextAsync("b");
                sub1.Cancel();
                sub2.Cancel();
                await p1Sub.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Unzip_must_work_with_Zip()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var c1 = this.CreateManualSubscriberProbe<(int, string)>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var zip = b.Add(new Zip<int, string>());
                    var unzip = b.Add(new UnZip<int, string>());
                    var source =
                        Source.From(new[]
                        {
                            new KeyValuePair<int, string>(1, "a"),
                            new KeyValuePair<int, string>(2, "b"),
                            new KeyValuePair<int, string>(3, "c")
                        });

                    b.From(source).To(unzip.In);
                    b.From(unzip.Out0).To(zip.In0);
                    b.From(unzip.Out1).To(zip.In1);
                    b.From(zip.Out).To(Sink.FromSubscriber(c1));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var sub1 = await c1.ExpectSubscriptionAsync();
                sub1.Request(5);
                await c1.ExpectNextAsync((1, "a"));
                await c1.ExpectNextAsync((2, "b"));
                await c1.ExpectNextAsync((3, "c"));
                await c1.ExpectCompleteAsync();
            }, Materializer);
        }
    }
}
