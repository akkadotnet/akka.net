//-----------------------------------------------------------------------
// <copyright file="FlowGroupBySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.Dsl.Internal;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Reactive.Streams;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class FlowGroupBySpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowGroupBySpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 2);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private sealed class StreamPuppet
        {
            private readonly TestSubscriber.ManualProbe<int> _probe;
            private readonly ISubscription _subscription;

            public StreamPuppet(IPublisher<int> p, TestKitBase kit)
            {
                _probe = kit.CreateManualSubscriberProbe<int>();
                p.Subscribe(_probe);
                _subscription = _probe.ExpectSubscription();
            }

            public void Request(int demand) => _subscription.Request(demand);

            public void ExpectNext(int element) => _probe.ExpectNext(element);

            public void ExpectNoMsg(TimeSpan max) => _probe.ExpectNoMsg(max);

            public void ExpectComplete() => _probe.ExpectComplete();

            public void ExpectError(Exception ex) => _probe.ExpectError().Should().Be(ex);

            public void Cancel() => _subscription.Cancel();
        }

        private void WithSubstreamsSupport(int groupCount = 2, int elementCount = 6, int maxSubstream = -1,
            Action<TestSubscriber.ManualProbe<(int, Source<int, NotUsed>)>, ISubscription, Func<int, Source<int, NotUsed>>> run = null)
        {

            var source = Source.From(Enumerable.Range(1, elementCount)).RunWith(Sink.AsPublisher<int>(false), Materializer);
            var max = maxSubstream > 0 ? maxSubstream : groupCount;
            var groupStream =
                Source.FromPublisher(source)
                    .GroupBy(max, x => x % groupCount)
                    .Lift(x => x % groupCount)
                    .RunWith(Sink.AsPublisher<(int, Source<int, NotUsed>)>(false), Materializer);
            var masterSubscriber = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();

            groupStream.Subscribe(masterSubscriber);
            var masterSubscription = masterSubscriber.ExpectSubscription();

            run?.Invoke(masterSubscriber, masterSubscription, expectedKey =>
            {
                masterSubscription.Request(1);
                var tuple = masterSubscriber.ExpectNext();
                tuple.Item1.Should().Be(expectedKey);
                return tuple.Item2;
            });
        }

        private ByteString RandomByteString(int size)
        {
            var a = new byte[size];
            ThreadLocalRandom.Current.NextBytes(a);
            return ByteString.FromBytes(a);
        }

        [Fact]
        public void GroupBy_must_work_in_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                WithSubstreamsSupport(2, run: (masterSubscriber, masterSubscription, getSubFlow) =>
                {
                    var s1 = new StreamPuppet(getSubFlow(1).RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                    masterSubscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                    s1.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                    s1.Request(1);
                    s1.ExpectNext(1);
                    s1.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                    var s2 = new StreamPuppet(getSubFlow(0).RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                    s2.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                    s2.Request(2);
                    s2.ExpectNext(2);
                    // Important to request here on the OTHER stream because the buffer space is exactly one without the fanout box
                    s1.Request(1);
                    s2.ExpectNext(4);

                    s2.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                    s1.ExpectNext(3);

                    s2.Request(1);
                    // Important to request here on the OTHER stream because the buffer space is exactly one without the fanout box
                    s1.Request(1);
                    s2.ExpectNext(6);
                    s2.ExpectComplete();

                    s1.ExpectNext(5);
                    s1.ExpectComplete();
                    masterSubscription.Request(1);
                    masterSubscriber.ExpectComplete();
                });
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_work_in_normal_user_scenario()
        {
            var source = Source.From(new[] { "Aaa", "Abb", "Bcc", "Cdd", "Cee" })
                .GroupBy(3, s => s.Substring(0, 1))
                .Grouped(10)
                .MergeSubstreams()
                .Grouped(10);
            var task =
                ((Source<IEnumerable<IEnumerable<string>>, NotUsed>)source).RunWith(
                    Sink.First<IEnumerable<IEnumerable<string>>>(), Materializer);
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.OrderBy(e => e.First())
                .ShouldBeEquivalentTo(new[] { new[] { "Aaa", "Abb" }, new[] { "Bcc" }, new[] { "Cdd", "Cee" } });
        }

        [Fact]
        public void GroupBy_must_fail_when_key_function_returns_null()
        {
            var source = (Source<IEnumerable<string>, NotUsed>)Source.From(new[] { "Aaa", "Abb", "Bcc", "Cdd", "Cee" })
                .GroupBy(3, s => s.StartsWith("A") ? null : s.Substring(0, 1))
                .Grouped(10)
                .MergeSubstreams();
            var down = source.RunWith(this.SinkProbe<IEnumerable<string>>(), Materializer);
            down.Request(1);
            var ex = down.ExpectError();
            ex.Message.Should().Contain("Key cannot be null");
            ex.Should().BeOfType<ArgumentNullException>();
        }

        [Fact]
        public void GroupBy_must_support_cancelling_substreams()
        {
            this.AssertAllStagesStopped(() =>
            {
                WithSubstreamsSupport(2, run: (masterSubscriber, masterSubscription, getSubFlow) =>
                {
                    new StreamPuppet(getSubFlow(1).RunWith(Sink.AsPublisher<int>(false), Materializer), this).Cancel();
                    var substream = new StreamPuppet(getSubFlow(0).RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                    substream.Request(2);
                    substream.ExpectNext(2);
                    substream.ExpectNext(4);
                    substream.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                    substream.Request(2);
                    substream.ExpectNext(6);
                    substream.ExpectComplete();

                    masterSubscription.Request(1);
                    masterSubscriber.ExpectComplete();
                });
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_accept_cancellation_of_master_stream_when_not_consume_anything()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisherProbe = this.CreateManualPublisherProbe<int>();
                var publisher =
                    Source.FromPublisher(publisherProbe)
                        .GroupBy(2, x => x % 2)
                        .Lift(x => x % 2)
                        .RunWith(Sink.AsPublisher<(int, Source<int, NotUsed>)>(false), Materializer);
                var subscriber = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();
                publisher.Subscribe(subscriber);

                var upstreamSubscription = publisherProbe.ExpectSubscription();
                var downstreamSubscription = subscriber.ExpectSubscription();
                downstreamSubscription.Cancel();
                upstreamSubscription.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_work_with_empty_input_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher =
                    Source.From(new List<int>())
                        .GroupBy(2, x => x % 2)
                        .Lift(x => x % 2)
                        .RunWith(Sink.AsPublisher<(int, Source<int, NotUsed>)>(false), Materializer);
                var subscriber = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();
                publisher.Subscribe(subscriber);

                subscriber.ExpectSubscriptionAndComplete();
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_abort_onError_from_upstream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisherProbe = this.CreateManualPublisherProbe<int>();
                var publisher =
                    Source.FromPublisher(publisherProbe)
                        .GroupBy(2, x => x % 2)
                        .Lift(x => x % 2)
                        .RunWith(Sink.AsPublisher<(int, Source<int, NotUsed>)>(false), Materializer);
                var subscriber = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();
                publisher.Subscribe(subscriber);

                var upstreamSubscription = publisherProbe.ExpectSubscription();
                var downstreamSubscription = subscriber.ExpectSubscription();
                downstreamSubscription.Request(100);

                var ex = new TestException("test");
                upstreamSubscription.SendError(ex);
                subscriber.ExpectError().Should().Be(ex);
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_abort_onError_from_upstream_when_substreams_are_running()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisherProbe = this.CreateManualPublisherProbe<int>();
                var publisher =
                    Source.FromPublisher(publisherProbe)
                        .GroupBy(2, x => x % 2)
                        .Lift(x => x % 2)
                        .RunWith(Sink.AsPublisher<(int, Source<int, NotUsed>)>(false), Materializer);
                var subscriber = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();
                publisher.Subscribe(subscriber);

                var upstreamSubscription = publisherProbe.ExpectSubscription();
                var downstreamSubscription = subscriber.ExpectSubscription();
                downstreamSubscription.Request(100);
                upstreamSubscription.SendNext(1);
                var substream = subscriber.ExpectNext().Item2;
                var substreamPuppet = new StreamPuppet(substream.RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                substreamPuppet.Request(1);
                substreamPuppet.ExpectNext(1);

                var ex = new TestException("test");
                upstreamSubscription.SendError(ex);

                substreamPuppet.ExpectError(ex);
                subscriber.ExpectError().Should().Be(ex);
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_fail_stream_when_GroupBy_function_throws()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisherProbe = this.CreateManualPublisherProbe<int>();
                var ex = new TestException("test");
                var publisher = Source.FromPublisher(publisherProbe).GroupBy(2, i =>
                {
                    if (i == 2)
                        throw ex;
                    return i % 2;
                })
                    .Lift(x => x % 2)
                    .RunWith(Sink.AsPublisher<(int, Source<int, NotUsed>)>(false), Materializer);


                var subscriber = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();
                publisher.Subscribe(subscriber);

                var upstreamSubscription = publisherProbe.ExpectSubscription();
                var downstreamSubscription = subscriber.ExpectSubscription();
                downstreamSubscription.Request(100);

                upstreamSubscription.SendNext(1);

                var substream = subscriber.ExpectNext().Item2;
                var substreamPuppet = new StreamPuppet(substream.RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                substreamPuppet.Request(1);
                substreamPuppet.ExpectNext(1);

                upstreamSubscription.SendNext(2);
                subscriber.ExpectError().Should().Be(ex);
                substreamPuppet.ExpectError(ex);
                upstreamSubscription.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_resume_stream_when_GroupBy_function_throws()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisherProbe = this.CreateManualPublisherProbe<int>();
                var ex = new TestException("test");
                var publisher = Source.FromPublisher(publisherProbe).GroupBy(2, i =>
                {
                    if (i == 2)
                        throw ex;
                    return i % 2;
                })
                    .Lift(x => x % 2)
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .RunWith(Sink.AsPublisher<(int, Source<int, NotUsed>)>(false), Materializer);

                var subscriber = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();
                publisher.Subscribe(subscriber);

                var upstreamSubscription = publisherProbe.ExpectSubscription();
                var downstreamSubscription = subscriber.ExpectSubscription();
                downstreamSubscription.Request(100);

                upstreamSubscription.SendNext(1);

                var substream = subscriber.ExpectNext().Item2;
                var substreamPuppet1 = new StreamPuppet(substream.RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                substreamPuppet1.Request(10);
                substreamPuppet1.ExpectNext(1);

                upstreamSubscription.SendNext(2);
                upstreamSubscription.SendNext(4);

                var substream2 = subscriber.ExpectNext().Item2;
                var substreamPuppet2 = new StreamPuppet(substream2.RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                substreamPuppet2.Request(10);
                substreamPuppet2.ExpectNext(4);

                upstreamSubscription.SendNext(3);
                substreamPuppet1.ExpectNext(3);

                upstreamSubscription.SendNext(6);
                substreamPuppet2.ExpectNext(6);

                upstreamSubscription.SendComplete();
                subscriber.ExpectComplete();
                substreamPuppet1.ExpectComplete();
                substreamPuppet2.ExpectComplete();

            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_pass_along_early_cancellation()
        {
            this.AssertAllStagesStopped(() =>
            {
                var up = this.CreateManualPublisherProbe<int>();
                var down = this.CreateManualSubscriberProbe<(int, Source<int, NotUsed>)>();

                var flowSubscriber =
                    Source.AsSubscriber<int>()
                        .GroupBy(2, x => x % 2)
                        .Lift(x => x % 2)
                        .To(Sink.FromSubscriber(down)).Run(Materializer);

                var downstream = down.ExpectSubscription();
                downstream.Cancel();
                up.Subscribe(flowSubscriber);
                var upSub = up.ExpectSubscription();
                upSub.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_fail_when_exceeding_maxSubstreams()
        {
            this.AssertAllStagesStopped(() =>
            {
                var f = Flow.Create<int>().GroupBy(1, x => x % 2).PrefixAndTail(0).MergeSubstreams();
                var t = ((Flow<int, (IImmutableList<int>, Source<int, NotUsed>), NotUsed>)f)
                    .RunWith(this.SourceProbe<int>(), this.SinkProbe<(IImmutableList<int>, Source<int, NotUsed>)>(), Materializer);
                var up = t.Item1;
                var down = t.Item2;

                down.Request(2);

                up.SendNext(1);
                var first = down.ExpectNext();
                var s1 = new StreamPuppet(first.Item2.RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                s1.Request(1);
                s1.ExpectNext(1);

                up.SendNext(2);
                var ex = down.ExpectError();
                ex.Message.Should().Contain("too many substreams");
                s1.ExpectError(ex);
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_emit_subscribe_before_completed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var source = (Source<Source<int, NotUsed>, NotUsed>)Source.Single(0).GroupBy(1, _ => "all")
                    .PrefixAndTail(0)
                    .Select(t => t.Item2)
                    .ConcatSubstream();
                var futureGroupSource = source.RunWith(Sink.First<Source<int, NotUsed>>(), Materializer);

                var publisher = futureGroupSource.AwaitResult().RunWith(Sink.AsPublisher<int>(false), Materializer);
                var probe = this.CreateSubscriberProbe<int>();
                publisher.Subscribe(probe);
                var subscription = probe.ExpectSubscription();
                subscription.Request(1);
                probe.ExpectNext(0);
                probe.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_work_under_fuzzing_stress_test()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisherProbe = this.CreateManualPublisherProbe<ByteString>();
                var subscriber = this.CreateManualSubscriberProbe<IEnumerable<byte>>();

                var firstGroup = (Source<IEnumerable<byte>, NotUsed>)Source.FromPublisher(publisherProbe)
                    .GroupBy(256, element => element[0])
                    .Select(b => b.Reverse())
                    .MergeSubstreams();
                var secondGroup = (Source<IEnumerable<byte>, NotUsed>)firstGroup.GroupBy(256, bytes => bytes.First())
                    .Select(b => b.Reverse())
                    .MergeSubstreams();
                var publisher = secondGroup.RunWith(Sink.AsPublisher<IEnumerable<byte>>(false), Materializer);
                publisher.Subscribe(subscriber);

                var upstreamSubscription = publisherProbe.ExpectSubscription();
                var downstreamSubscription = subscriber.ExpectSubscription();

                downstreamSubscription.Request(300);
                for (var i = 1; i <= 300; i++)
                {
                    var byteString = RandomByteString(10);
                    upstreamSubscription.ExpectRequest();
                    upstreamSubscription.SendNext(byteString);
                    subscriber.ExpectNext().ShouldBeEquivalentTo(byteString);
                }

                upstreamSubscription.SendComplete();
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_Work_if_pull_is_exercised_from_both_substream_and_main()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstream = this.CreatePublisherProbe<int>();
                var downstreamMaster = this.CreateSubscriberProbe<Source<int, NotUsed>>();

                Source.FromPublisher(upstream)
                    .Via(new GroupBy<int, bool>(2, element => element == 0))
                    .RunWith(Sink.FromSubscriber(downstreamMaster), Materializer);

                var substream = this.CreateSubscriberProbe<int>();

                downstreamMaster.Request(1);
                upstream.SendNext(1);
                downstreamMaster.ExpectNext().RunWith(Sink.FromSubscriber(substream), Materializer);

                // Read off first buffered element from subsource
                substream.Request(1);
                substream.ExpectNext(1);

                // Both will attempt to pull upstream
                substream.Request(1);
                substream.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                downstreamMaster.Request(1);
                downstreamMaster.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                // Cleanup, not part of the actual test
                substream.Cancel();
                downstreamMaster.Cancel();
                upstream.SendComplete();
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_work_with_random_demand()
        {
            this.AssertAllStagesStopped(() =>
            {
                var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(1, 1);
                var materializer = Sys.Materializer(settings);

                var props = new RandomDemandProperties
                {
                    Kit = this
                };
                Enumerable.Range(0, 100)
                    .ToList()
                    .ForEach(_ => props.Probes.Add(new TaskCompletionSource<TestSubscriber.Probe<ByteString>>()));

                var map = new Dictionary<int, SubFlowState>();

                var publisherProbe = this.CreateManualPublisherProbe<ByteString>();
                var probeShape = new SinkShape<ByteString>(new Inlet<ByteString>("ProbeSink.in"));
                var probeSink = new ProbeSink(probeShape, props, Attributes.None);
                Source.FromPublisher(publisherProbe)
                    .GroupBy(100, element => Math.Abs(element[0] % 100))
                    .To(new Sink<ByteString, TestSubscriber.Probe<ByteString>>(probeSink))
                    .Run(materializer);

                var upstreamSubscription = publisherProbe.ExpectSubscription();

                for (var i = 1; i <= 400; i++)
                {
                    var byteString = RandomByteString(10);
                    var index = Math.Abs(byteString[0] % 100);

                    upstreamSubscription.ExpectRequest();
                    upstreamSubscription.SendNext(byteString);

                    if (map.TryGetValue(index, out var state))
                    {
                        if (state.FirstElement != null) //first element in subFlow 
                        {
                            if (!state.HasDemand)
                                props.BlockingNextElement = byteString;
                            RandomDemand(map, props);
                        }
                        else if (state.HasDemand)
                        {
                            if (props.BlockingNextElement == null)
                            {
                                state.Probe.ExpectNext().ShouldBeEquivalentTo(byteString);
                                map[index] = new SubFlowState(state.Probe, false, null);
                                RandomDemand(map, props);
                            }
                            else
                                true.ShouldBeFalse("INVALID CASE");
                        }
                        else
                        {
                            props.BlockingNextElement = byteString;
                            RandomDemand(map, props);
                        }
                    }
                    else
                    {
                        var probe = props.Probes[props.ProbesReaderTop].Task.AwaitResult();
                        props.ProbesReaderTop++;
                        map[index] = new SubFlowState(probe, false, byteString);
                        //stream automatically requests next element 
                    }
                }
                upstreamSubscription.SendComplete();
            }, Materializer);
        }

        private sealed class SubFlowState
        {
            public SubFlowState(TestSubscriber.Probe<ByteString> probe, bool hasDemand, ByteString firstElement)
            {
                Probe = probe;
                HasDemand = hasDemand;
                FirstElement = firstElement;
            }

            public TestSubscriber.Probe<ByteString> Probe { get; }

            public bool HasDemand { get; }

            public ByteString FirstElement { get; }
        }

        private sealed class ProbeSink : SinkModule<ByteString, TestSubscriber.Probe<ByteString>>
        {
            private readonly RandomDemandProperties _properties;

            public ProbeSink(SinkShape<ByteString> shape, RandomDemandProperties properties, Attributes attributes) : base(shape)
            {
                _properties = properties;
                Attributes = attributes;
            }

            public override Attributes Attributes { get; }

            public override IModule WithAttributes(Attributes attributes)
            {
                return new ProbeSink(AmendShape(attributes), _properties, attributes);
            }

            protected override SinkModule<ByteString, TestSubscriber.Probe<ByteString>> NewInstance(SinkShape<ByteString> shape)
            {
                return new ProbeSink(shape, _properties, Attributes);
            }

            public override object Create(MaterializationContext context, out TestSubscriber.Probe<ByteString> materializer)
            {
                var promise = _properties.Probes[_properties.ProbesWriterTop];
                var probe = TestSubscriber.CreateSubscriberProbe<ByteString>(_properties.Kit);
                promise.SetResult(probe);
                _properties.ProbesWriterTop++;
                materializer = probe;
                return probe;
            }
        }

        private sealed class RandomDemandProperties
        {
            public TestKitBase Kit { get; set; }

            public int ProbesWriterTop { get; set; }

            public int ProbesReaderTop { get; set; }

            public List<TaskCompletionSource<TestSubscriber.Probe<ByteString>>> Probes { get; } =
                new List<TaskCompletionSource<TestSubscriber.Probe<ByteString>>>(100);

            public ByteString BlockingNextElement { get; set; }
        }

        private void RandomDemand(Dictionary<int, SubFlowState> map, RandomDemandProperties props)
        {
            while (true)
            {

                var nextIndex = ThreadLocalRandom.Current.Next(0, map.Count);
                var key = map.Keys.ToArray()[nextIndex];
                if (!map[key].HasDemand)
                {
                    var state = map[key];
                    map[key] = new SubFlowState(state.Probe, true, state.FirstElement);

                    state.Probe.Request(1);

                    // need to verify elements that are first element in subFlow or is in nextElement buffer before 
                    // pushing next element from upstream 
                    if (state.FirstElement != null)
                    {
                        state.Probe.ExpectNext().ShouldBeEquivalentTo(state.FirstElement);
                        map[key] = new SubFlowState(state.Probe, false, null);
                    }
                    else if (props.BlockingNextElement != null && Math.Abs(props.BlockingNextElement[0] % 100) == key)
                    {
                        state.Probe.ExpectNext().ShouldBeEquivalentTo(props.BlockingNextElement);
                        props.BlockingNextElement = null;
                        map[key] = new SubFlowState(state.Probe, false, null);
                    }
                    else if (props.BlockingNextElement == null)
                     break;
                }
            }

        }
    }
}
