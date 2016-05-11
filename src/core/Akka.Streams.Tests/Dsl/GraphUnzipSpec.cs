//-----------------------------------------------------------------------
// <copyright file="GraphUnzipSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
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
        public void A_Unzip_must_unzip_to_two_subscribers()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c1 = TestSubscriber.CreateManualProbe<int>(this);
                var c2 = TestSubscriber.CreateManualProbe<string>(this);

                RunnableGraph.FromGraph(GraphDsl.Create<ClosedShape, NotUsed>(b =>
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
                        .Via(Flow.Create<int>().Buffer(16, OverflowStrategy.Backpressure).Select(x => x*2))
                        .To(Sink.FromSubscriber(c1));
                    b.From(unzip.Out1)
                        .Via(Flow.Create<string>().Buffer(16, OverflowStrategy.Backpressure))
                        .To(Sink.FromSubscriber(c2));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var sub1 = c1.ExpectSubscription();
                var sub2 = c2.ExpectSubscription();

                sub1.Request(1);
                sub2.Request(2);
                c1.ExpectNext(1*2);
                c1.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                c2.ExpectNext("a", "b");
                c2.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                sub1.Request(3);
                c1.ExpectNext(2*2, 3*2);
                c1.ExpectComplete();
                sub2.Request(3);
                c2.ExpectNext("c");
                c2.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Unzip_must_produce_to_right_downstream_even_though_left_downstream_cancels()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c1 = TestSubscriber.CreateManualProbe<int>(this);
                var c2 = TestSubscriber.CreateManualProbe<string>(this);

                RunnableGraph.FromGraph(GraphDsl.Create<ClosedShape, NotUsed>(b =>
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

                var sub1 = c1.ExpectSubscription();
                var sub2 = c2.ExpectSubscription();

                sub1.Cancel();
                sub2.Request(3);
                c2.ExpectNext("a", "b", "c");
                c2.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Unzip_must_produce_to_left_downstream_even_though_right_downstream_cancels()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c1 = TestSubscriber.CreateManualProbe<int>(this);
                var c2 = TestSubscriber.CreateManualProbe<string>(this);

                RunnableGraph.FromGraph(GraphDsl.Create<ClosedShape, NotUsed>(b =>
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

                var sub1 = c1.ExpectSubscription();
                var sub2 = c2.ExpectSubscription();

                sub2.Cancel();
                sub1.Request(3);
                c1.ExpectNext(1, 2, 3);
                c1.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void A_Unzip_must_cancel_upstream_when_downstream_cancel()
        {
            this.AssertAllStagesStopped(() =>
            {
                var p1 = TestPublisher.CreateManualProbe<KeyValuePair<int, string>>(this);
                var c1 = TestSubscriber.CreateManualProbe<int>(this);
                var c2 = TestSubscriber.CreateManualProbe<string>(this);

                RunnableGraph.FromGraph(GraphDsl.Create<ClosedShape, NotUsed>(b =>
                {
                    var unzip = b.Add(new UnZip<int, string>());
                    var source = Source.FromPublisher(p1.Publisher);

                    b.From(source).To(unzip.In);
                    b.From(unzip.Out0).To(Sink.FromSubscriber(c1));
                    b.From(unzip.Out1).To(Sink.FromSubscriber(c2));

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var p1Sub = p1.ExpectSubscription();
                var sub1 = c1.ExpectSubscription();
                var sub2 = c2.ExpectSubscription();

                sub1.Request(3);
                sub2.Request(3);
                p1.ExpectRequest(p1Sub, 16);
                p1Sub.SendNext(new KeyValuePair<int, string>(1, "a"));
                c1.ExpectNext(1);
                c2.ExpectNext("a");
                p1Sub.SendNext(new KeyValuePair<int, string>(2, "b"));
                c1.ExpectNext(2);
                c2.ExpectNext("b");
                sub1.Cancel();
                sub2.Cancel();
                p1Sub.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void A_Unzip_must_work_with_Zip()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c1 = TestSubscriber.CreateManualProbe<Tuple<int, string>>(this);

                RunnableGraph.FromGraph(GraphDsl.Create<ClosedShape, NotUsed>(b =>
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

                var sub1 = c1.ExpectSubscription();
                sub1.Request(5);
                c1.ExpectNext(Tuple.Create(1, "a"));
                c1.ExpectNext(Tuple.Create(2, "b"));
                c1.ExpectNext(Tuple.Create(3, "c"));
                c1.ExpectComplete();
            }, Materializer);
        }
    }
}
