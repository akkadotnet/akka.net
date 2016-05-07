//-----------------------------------------------------------------------
// <copyright file="SourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Streams;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class SourceSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public SourceSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public void Single_Source_must_produce_element()
        {
            var p = Source.Single(1).RunWith(Sink.AsPublisher<int>(false), Materializer);
            var c = TestSubscriber.CreateManualProbe<int>(this);
            p.Subscribe(c);
            var sub = c.ExpectSubscription();
            sub.Request(1);
            c.ExpectNext(1);
            c.ExpectComplete();
        }

        [Fact]
        public void Single_Source_must_reject_later_subscriber()
        {
            var p = Source.Single(1).RunWith(Sink.AsPublisher<int>(false), Materializer);
            var c1 = TestSubscriber.CreateManualProbe<int>(this);
            var c2 = TestSubscriber.CreateManualProbe<int>(this);
            p.Subscribe(c1);

            var sub1 = c1.ExpectSubscription();
            sub1.Request(1);
            c1.ExpectNext(1);
            c1.ExpectComplete();

            p.Subscribe(c2);
            c2.ExpectSubscriptionAndError();
        }

        [Fact]
        public void Empty_Source_must_complete_immediately()
        {
            var p = Source.Empty<int>().RunWith(Sink.AsPublisher<int>(false), Materializer);
            var c = TestSubscriber.CreateManualProbe<int>(this);
            p.Subscribe(c);
            c.ExpectSubscriptionAndComplete();

            //reject additional subscriber
            var c2 = TestSubscriber.CreateManualProbe<int>(this);
            p.Subscribe(c2);
            c2.ExpectSubscriptionAndError();
        }

        [Fact]
        public void Failed_Source_must_emit_error_immediately()
        {
            var ex = new SystemException();
            var p = Source.Failed<int>(ex).RunWith(Sink.AsPublisher<int>(false), Materializer);
            var c = TestSubscriber.CreateManualProbe<int>(this);
            p.Subscribe(c);
            c.ExpectSubscriptionAndError();

            //reject additional subscriber
            var c2 = TestSubscriber.CreateManualProbe<int>(this);
            p.Subscribe(c2);
            c2.ExpectSubscriptionAndError();
        }

        [Fact]
        public void Maybe_Source_must_complete_materialized_future_with_None_when_stream_cancels()
        {
            this.AssertAllStagesStopped(() =>
            {
                var neverSource = Source.Maybe<object>();
                var pubSink = Sink.AsPublisher<object>(false);

                var t = neverSource.ToMaterialized(pubSink, Keep.Both).Run(Materializer);
                var f = t.Item1;
                var neverPub = t.Item2;

                var c = TestSubscriber.CreateManualProbe<object>(this);
                neverPub.Subscribe(c);
                var subs = c.ExpectSubscription();

                subs.Request(1000);
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(300));

                subs.Cancel();
                f.Task.Wait(500).Should().BeTrue();
                f.Task.Result.Should().Be(null);
            }, Materializer);
        }

        [Fact]
        public void Maybe_Source_must_allow_external_triggering_of_empty_completion()
        {
            this.AssertAllStagesStopped(() =>
            {
                var neverSource = Source.Maybe<int>().Where(_ => false);
                var counterSink = Sink.Fold<int, int>(0, (acc, _) => acc + 1);

                var t = neverSource.ToMaterialized(counterSink, Keep.Both).Run(Materializer);
                var neverPromise = t.Item1;
                var counterFuture = t.Item2;
                
                //external cancellation
                neverPromise.TrySetResult(0).Should().BeTrue();

                counterFuture.Wait(500).Should().BeTrue();
                counterFuture.Result.Should().Be(0);
            }, Materializer);
        }

        [Fact]
        public void Maybe_Source_must_allow_external_triggering_of_non_empty_completion()
        {
            this.AssertAllStagesStopped(() =>
            {
                var neverSource = Source.Maybe<int>();
                var counterSink = Sink.First<int>();

                var t = neverSource.ToMaterialized(counterSink, Keep.Both).Run(Materializer);
                var neverPromise = t.Item1;
                var counterFuture = t.Item2;

                //external cancellation
                neverPromise.TrySetResult(6).Should().BeTrue();

                counterFuture.Wait(500).Should().BeTrue();
                counterFuture.Result.Should().Be(6);
            }, Materializer);
        }

        [Fact]
        public void Maybe_Source_must_allow_external_triggering_of_OnError()
        {
            this.AssertAllStagesStopped(() =>
            {
                var neverSource = Source.Maybe<int>();
                var counterSink = Sink.First<int>();

                var t = neverSource.ToMaterialized(counterSink, Keep.Both).Run(Materializer);
                var neverPromise = t.Item1;
                var counterFuture = t.Item2;

                //external cancellation
                neverPromise.SetException(new Exception("Boom"));

                counterFuture.Invoking(f => f.Wait(500)).ShouldThrow<Exception>()
                    .WithMessage("Boom");
            }, Materializer);
        }

        [Fact]
        public void Composite_Source_must_merge_from_many_inputs()
        {
            var probes = Enumerable.Range(1, 5).Select(_ => TestPublisher.CreateManualProbe<int>(this)).ToList();
            var source = Source.AsSubscriber<int>();
            var outProbe = TestSubscriber.CreateManualProbe<int>(this);

            var s =
                Source.FromGraph(GraphDsl.Create(source, source, source, source, source,
                    (a, b, c, d, e) => new[] {a, b, c, d, e},
                    (b, i0, i1, i2, i3, i4) =>
                    {
                        var m = b.Add(new Merge<int>(5));
                        b.From(i0.Outlet).To(m.In(0));
                        b.From(i1.Outlet).To(m.In(1));
                        b.From(i2.Outlet).To(m.In(2));
                        b.From(i3.Outlet).To(m.In(3));
                        b.From(i4.Outlet).To(m.In(4));
                        return new SourceShape<int>(m.Out);
                    })).To(Sink.FromSubscriber(outProbe)).Run(Materializer);

            for (var i = 0; i < 5; i++)
                probes[i].Subscribe(s[i]);
            var sub = outProbe.ExpectSubscription();
            sub.Request(10);

            for (var i = 0; i < 5; i++)
            {
                var subscription = probes[i].ExpectSubscription();
                subscription.ExpectRequest();
                subscription.SendNext(i);
                subscription.SendComplete();
            }

            var gotten = new List<int>();
            for (var i = 0; i < 5; i++)
                gotten.Add(outProbe.ExpectNext());
            gotten.ShouldAllBeEquivalentTo(new[] {0, 1, 2, 3, 4});
            outProbe.ExpectComplete();
        }

        [Fact]
        public void Composite_Source_must_combine_from_many_inputs_with_simplified_API()
        {
            var probes = Enumerable.Range(1, 3).Select(_ => TestPublisher.CreateManualProbe<int>(this)).ToList();
            var source = probes.Select(Source.FromPublisher).ToList();
            var outProbe = TestSubscriber.CreateManualProbe<int>(this);

            Source.Combine(source[0], source[1], i => new Merge<int, int>(i), source[2])
                .To(Sink.FromSubscriber(outProbe))
                .Run(Materializer);

            var sub = outProbe.ExpectSubscription();
            sub.Request(3);

            for (var i = 0; i < 3; i++)
            {
                var s = probes[i].ExpectSubscription();
                s.ExpectRequest();
                s.SendNext(i);
                s.SendComplete();
            }

            var gotten = new List<int>();
            for (var i = 0; i < 3; i++)
                gotten.Add(outProbe.ExpectNext());
            gotten.ShouldAllBeEquivalentTo(new[] {0, 1, 2});
            outProbe.ExpectComplete();
        }

        [Fact]
        public void Composite_Source_must_combine_from_two_inputs_with_simplified_API()
        {
            var probes = Enumerable.Range(1, 2).Select(_ => TestPublisher.CreateManualProbe<int>(this)).ToList();
            var source = probes.Select(Source.FromPublisher).ToList();
            var outProbe = TestSubscriber.CreateManualProbe<int>(this);

            Source.Combine(source[0], source[1], i => new Merge<int, int>(i))
                .To(Sink.FromSubscriber(outProbe))
                .Run(Materializer);

            var sub = outProbe.ExpectSubscription();
            sub.Request(3);

            for (var i = 0; i < 2; i++)
            {
                var s = probes[i].ExpectSubscription();
                s.ExpectRequest();
                s.SendNext(i);
                s.SendComplete();
            }

            var gotten = new List<int>();
            for (var i = 0; i < 2; i++)
                gotten.Add(outProbe.ExpectNext());
            gotten.ShouldAllBeEquivalentTo(new[] {0, 1});
            outProbe.ExpectComplete();
        }

        [Fact]
        public void Repeat_Source_must_repeat_as_long_as_it_takes()
        {
            var f = Source.Repeat(42).Grouped(1000).RunWith(Sink.First<IEnumerable<int>>(), Materializer);
            f.Result.Should().HaveCount(1000).And.Match(x => x.All(i => i == 42));
        }

        private static readonly int[] Expected = {
            9227465, 5702887, 3524578, 2178309, 1346269, 832040, 514229, 317811, 196418, 121393, 75025, 46368, 28657, 17711,
            10946, 6765, 4181, 2584, 1597, 987, 610, 377, 233, 144, 89, 55, 34, 21, 13, 8, 5, 3, 2, 1, 1, 0
        };

        [Fact]
        public void Unfold_Source_must_generate_a_finite_fibonacci_sequence()
        {
            Source.Unfold(Tuple.Create(0, 1), tuple =>
            {
                var a = tuple.Item1;
                var b = tuple.Item2;
                if (a > 10000000)
                    return null;
                return Tuple.Create(Tuple.Create(b, a + b), a);
            }).RunFold(new LinkedList<int>(), (ints, i) =>
            {
                ints.AddFirst(i);
                return ints;
            }, Materializer).Result.Should().Equal(Expected);
        }

        [Fact]
        public void Unfold_Source_must_terminate_with_a_failure_if_there_is_an_exception_thrown()
        {
            EventFilter.Exception<SystemException>(message: "expected").ExpectOne(() =>
            {
                var task = Source.Unfold(Tuple.Create(0, 1), tuple =>
                {
                    var a = tuple.Item1;
                    var b = tuple.Item2;
                    if (a > 10000000)
                        throw new SystemException("expected");
                    return Tuple.Create(Tuple.Create(b, a + b), a);
                }).RunFold(new LinkedList<int>(), (ints, i) =>
                {
                    ints.AddFirst(i);
                    return ints;
                }, Materializer);
                task.Invoking(t => t.Wait(TimeSpan.FromSeconds(3)))
                    .ShouldThrow<SystemException>()
                    .WithMessage("expected");
            });
        }

        [Fact]
        public void Unfold_Source_must_generate_a_finite_fibonacci_sequence_asynchronously()
        {
            Source.UnfoldAsync(Tuple.Create(0, 1), tuple =>
            {
                var a = tuple.Item1;
                var b = tuple.Item2;
                if (a > 10000000)
                    return Task.FromResult<Tuple<Tuple<int, int>, int>>(null);
                return Task.FromResult(Tuple.Create(Tuple.Create(b, a + b), a));
            }).RunFold(new LinkedList<int>(), (ints, i) =>
            {
                ints.AddFirst(i);
                return ints;
            }, Materializer).Result.Should().Equal(Expected);
        }

        [Fact]
        public void Unfold_Source_must_generate_a_unboundeed_fibonacci_sequence()
        {
            Source.Unfold(Tuple.Create(0, 1), tuple =>
            {
                var a = tuple.Item1;
                var b = tuple.Item2;
                return Tuple.Create(Tuple.Create(b, a + b), a);
            })
            .Take(36)
            .RunFold(new LinkedList<int>(), (ints, i) =>
            {
                ints.AddFirst(i);
                return ints;
            }, Materializer).Result.Should().Equal(Expected);
        }

        [Fact]
        public void Iterator_Source_must_properly_iterate()
        {
            var expected = new[] {false, true, false, true, false, true, false, true, false, true }.ToList();
            Source.FromEnumerator(() => expected.GetEnumerator())
                .Grouped(10)
                .RunWith(Sink.First<IEnumerable<bool>>(), Materializer)
                .Result.Should()
                .Equal(expected);
        }

        [Fact]
        public void A_Source_must_suitably_override_attribute_handling_methods()
        {
            Source.Single(42).Async().AddAttributes(Attributes.None).Named("");
        }
    }
}
