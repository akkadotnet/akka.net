//-----------------------------------------------------------------------
// <copyright file="RetrySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2017 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class RetrySpec : Akka.TestKit.Xunit2.TestKit
    {
        private static readonly Result<int> FailedElement = Result.Failure<int>(new Exception("cooked failure"));

        private static Flow<Tuple<int, T>, Tuple<Result<int>, T>, NotUsed> RetryFlow<T>() =>
            Flow.Identity<Tuple<int, T>>()
                .Select(t => t.Item1 % 2 == 0
                    ? Tuple.Create(FailedElement, t.Item2)
                    : Tuple.Create(Result.Success(t.Item1 + 1), t.Item2));

        [Fact]
        public void Retry_should_retry_ints_according_to_their_parity()
        {
            var t = this.SourceProbe<int>()
                .Select(i => Tuple.Create(i, i))
                .Via(Retry.Create(RetryFlow<int>(), s => s < 42 ? Tuple.Create(s + 1, s + 1) : null))
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());

            var source = t.Item1;
            var sink = t.Item2;

            sink.Request(99);
            source.SendNext(1);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            source.SendNext(2);
            sink.ExpectNext().Item1.Value.Should().Be(4);
            source.SendNext(42);
            sink.ExpectNext().Item1.Should().Be(FailedElement);
            source.SendComplete();
            sink.ExpectComplete();
        }

        [Fact]
        public void Retry_descending_ints_until_success()
        {
            var t = this.SourceProbe<int>()
                .Select(i => Tuple.Create(i, Enumerable.Range(0, i).Reverse().Select(x => x * 2).Concat(new[] { i + 1 }).ToImmutableList()))
                .Via(Retry.Create(RetryFlow<ImmutableList<int>>(),
                    s => s.IsEmpty
                        ? throw new IllegalStateException("should not happen")
                        : Tuple.Create(s[0], s.RemoveAt(0))))
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, ImmutableList<int>>>(), Keep.Both)
                .Run(Sys.Materializer());

            var source = t.Item1;
            var sink = t.Item2;

            sink.Request(99);
            source.SendNext(1);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            source.SendNext(2);
            sink.ExpectNext().Item1.Value.Should().Be(4);
            source.SendNext(40);
            sink.ExpectNext().Item1.Value.Should().Be(42);
            source.SendComplete();
            sink.ExpectComplete();
        }

        [Fact]
        public void Retry_squares_by_division()
        {
            var t = this.SourceProbe<int>()
                .Select(i => Tuple.Create(i, i * i))
                .Via(Retry.Create(RetryFlow<int>(), s =>
                {
                    if (s % 4 == 0) return Tuple.Create(s / 2, s / 4);
                    var sqrt = (int)Math.Sqrt(s);
                    return Tuple.Create(sqrt, s);
                }))
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());

            var source = t.Item1;
            var sink = t.Item2;

            sink.Request(99);
            source.SendNext(1);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            source.SendNext(2);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            source.SendNext(4);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            sink.ExpectNoMsg(TimeSpan.FromSeconds(3));
            source.SendComplete();
            sink.ExpectComplete();
        }

        [Fact]
        public void Tolerate_killswitch_terminations_after_start()
        {
            var t = this.SourceProbe<int>()
                .ViaMaterialized(KillSwitches.Single<int>(), Keep.Both)
                .Select(i => Tuple.Create(i, i * i))
                .Via(Retry.Create(RetryFlow<int>(), x =>
                {
                    if (x % 4 == 0) return Tuple.Create(x / 2, x / 4);
                    var sqrt = (int)Math.Sqrt(x);
                    return Tuple.Create(sqrt, x);
                }))
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());

            var source = t.Item1.Item1;
            var killSwitch = t.Item1.Item2;
            var sink = t.Item2;

            sink.Request(99);
            source.SendNext(1);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            source.SendNext(2);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            killSwitch.Abort(FailedElement.Exception);
            sink.ExpectError().InnerException.Should().Be(FailedElement.Exception);
        }

        [Fact]
        public void Tolerate_killswitch_terminations_on_start()
        {
            var t = this.SourceProbe<int>()
                .ViaMaterialized(KillSwitches.Single<int>(), Keep.Right)
                .Select(i => Tuple.Create(i, i))
                .Via(Retry.Create(RetryFlow<int>(), x => Tuple.Create(x, x + 1)))
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());

            var killSwitch = t.Item1;
            var sink = t.Item2;

            sink.Request(99);
            killSwitch.Abort(FailedElement.Exception);
            sink.ExpectError().InnerException.Should().Be(FailedElement.Exception);
        }

        [Fact]
        public void Tolerate_killswitch_terminations_before_start()
        {
            var t = this.SourceProbe<int>()
                .ViaMaterialized(KillSwitches.Single<int>(), Keep.Right)
                .Select(i => Tuple.Create(i, i))
                .Via(Retry.Create(RetryFlow<int>(), x => Tuple.Create(x, x + 1)))
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());

            var killSwitch = t.Item1;
            var sink = t.Item2;

            killSwitch.Abort(FailedElement.Exception);
            sink.Request(1);
            sink.ExpectError().InnerException.Should().Be(FailedElement.Exception);
        }

        [Fact]
        public void Tolerate_killswitch_terminations_inside_the_flow_after_start()
        {
            var innerFlow = RetryFlow<int>().ViaMaterialized(KillSwitches.Single<Tuple<Result<int>, int>>(), Keep.Right);

            var t = this.SourceProbe<int>()
                .Select(i => Tuple.Create(i, i * i))
                .ViaMaterialized(Retry.Create(innerFlow, x =>
                {
                    if (x % 4 == 0) return Tuple.Create(x / 2, x / 4);
                    var sqrt = (int)Math.Sqrt(x);
                    return Tuple.Create(sqrt, x);
                }), Keep.Both)
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());

            var killSwitch = t.Item1.Item2;
            var sink = t.Item2;

            sink.Request(99);
            killSwitch.Abort(FailedElement.Exception);
            sink.ExpectError().InnerException.Should().Be(FailedElement.Exception);
        }

        [Fact]
        public void Tolerate_killswitch_terminations_inside_the_flow_on_start()
        {
            var innerFlow = RetryFlow<int>().ViaMaterialized(KillSwitches.Single<Tuple<Result<int>, int>>(), Keep.Right);

            var t = this.SourceProbe<int>()
                .Select(i => Tuple.Create(i, i))
                .ViaMaterialized(Retry.Create(innerFlow, x => Tuple.Create(x, x + 1)), Keep.Right)
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());

            var killSwitch = t.Item1;
            var sink = t.Item2;

            sink.Request(99);
            killSwitch.Abort(FailedElement.Exception);
            sink.ExpectError().InnerException.Should().Be(FailedElement.Exception);
        }

        [Fact]
        public void Tolerate_killswitch_terminations_inside_the_flow_before_start()
        {
            var innerFlow = RetryFlow<int>().ViaMaterialized(KillSwitches.Single<Tuple<Result<int>, int>>(), Keep.Right);

            var t = this.SourceProbe<int>()
                .Select(i => Tuple.Create(i, i))
                .ViaMaterialized(Retry.Create(innerFlow, x => Tuple.Create(x, x + 1)), Keep.Right)
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());

            var killSwitch = t.Item1;
            var sink = t.Item2;

            killSwitch.Abort(FailedElement.Exception);
            sink.Request(1);
            sink.ExpectError().InnerException.Should().Be(FailedElement.Exception);
        }

        [Fact]
        public void RetryConcat_should_swallow_failed_elements_that_are_retried_with_an_empty_seq()
        {
            var t = this.SourceProbe<int>()
                .Select(i => Tuple.Create(i, i))
                .Via(Retry.Concat(100, RetryFlow<int>(), _ => Enumerable.Empty<Tuple<int, int>>()))
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());

            var source = t.Item1;
            var sink = t.Item2;

            sink.Request(99);
            source.SendNext(1);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            source.SendNext(2);
            sink.ExpectNoMsg();
            source.SendNext(3);
            sink.ExpectNext().Item1.Value.Should().Be(4);
            source.SendNext(4);
            sink.ExpectNoMsg();
            source.SendComplete();
            sink.ExpectComplete();
        }

        [Fact]
        public void RetryConcat_should_concat_incremented_ints_and_modulo_3_incremented_ints_from_retries()
        {
            var t = this.SourceProbe<int>()
                .Select(i => Tuple.Create(i, i))
                .Via(Retry.Concat(100, RetryFlow<int>(), os =>
                {
                    var s = (os + 1) % 3;
                    if (os < 42) return new[] { Tuple.Create(os + 1, os + 1), Tuple.Create(s, s) };
                    if (os == 42) return new Tuple<int, int>[0];
                    return null;
                }))
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());

            var source = t.Item1;
            var sink = t.Item2;

            sink.Request(99);
            source.SendNext(1);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            source.SendNext(2);
            sink.ExpectNext().Item1.Value.Should().Be(4);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            source.SendNext(44);
            sink.ExpectNext().Item1.Value.Should().Be(FailedElement.Value);
            source.SendNext(42);
            sink.ExpectNoMsg();
            source.SendComplete();
            sink.ExpectComplete();
        }

        [Fact]
        public void RetryConcat_squares_by_division()
        {
            var t = this.SourceProbe<int>()
                .Select(i => Tuple.Create(i, i * i))
                .Via(Retry.Concat(100, RetryFlow<int>(), x =>
                {
                    if (x % 4 == 0) return new[] { Tuple.Create(x / 2, x / 4) };
                    var sqrt = (int)Math.Sqrt(x);
                    return new[] { Tuple.Create(sqrt, x) };
                }))
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());

            var source = t.Item1;
            var sink = t.Item2;

            sink.Request(99);
            source.SendNext(1);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            source.SendNext(2);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            source.SendNext(4);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            sink.ExpectNoMsg(TimeSpan.FromSeconds(3));
            source.SendComplete();
            sink.ExpectComplete();
        }

        [Fact]
        public void RetryConcat_tolerate_killswitch_terminations_after_start()
        {
            var t = this.SourceProbe<int>()
                .ViaMaterialized(KillSwitches.Single<int>(), Keep.Both)
                .Select(i => Tuple.Create(i, i * i))
                .Via(Retry.Concat(100, RetryFlow<int>(), x =>
                {
                    if (x % 4 == 0) return new[] { Tuple.Create(x / 2, x / 4) };
                    var sqrt = (int)Math.Sqrt(x);
                    return new[] { Tuple.Create(sqrt, x) };
                }))
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());

            var source = t.Item1.Item1;
            var killswitch = t.Item1.Item2;
            var sink = t.Item2;

            sink.Request(99);
            source.SendNext(1);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            source.SendNext(2);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            killswitch.Abort(FailedElement.Exception);
            sink.ExpectError().InnerException.Should().Be(FailedElement.Exception);
        }

        [Fact]
        public void RetryConcat_tolerate_killswitch_terminations_on_start()
        {
            var t = this.SourceProbe<int>()
                .ViaMaterialized(KillSwitches.Single<int>(), Keep.Right)
                .Select(i => Tuple.Create(i, i))
                .Via(Retry.Concat(100, RetryFlow<int>(), x => new[] { Tuple.Create(x, x + 1) }))
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());

            var killSwitch = t.Item1;
            var sink = t.Item2;

            sink.Request(99);
            killSwitch.Abort(FailedElement.Exception);
            sink.ExpectError().InnerException.Should().Be(FailedElement.Exception);
        }

        [Fact]
        public void RetryConcat_tolerate_killswitch_terminations_before_start()
        {
            var t = this.SourceProbe<int>()
                .ViaMaterialized(KillSwitches.Single<int>(), Keep.Right)
                .Select(i => Tuple.Create(i, i))
                .Via(Retry.Concat(100, RetryFlow<int>(), x => new[] { Tuple.Create(x, x + 1) }))
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());

            var killSwitch = t.Item1;
            var sink = t.Item2;

            killSwitch.Abort(FailedElement.Exception);
            sink.Request(1);
            sink.ExpectError().InnerException.Should().Be(FailedElement.Exception);
        }

        [Fact]
        public void RetryConcat_tolerate_killswitch_terminations_inside_the_flow_after_start()
        {
            var innerFlow = RetryFlow<int>().ViaMaterialized(KillSwitches.Single<Tuple<Result<int>, int>>(), Keep.Right);

            var t = this.SourceProbe<int>()
                .Select(i => Tuple.Create(i, i * i))
                .ViaMaterialized(Retry.Concat(100, innerFlow, x =>
                {
                    if (x % 4 == 0) return new[] { Tuple.Create(x / 2, x / 4) };
                    var sqrt = (int)Math.Sqrt(x);
                    return new[] { Tuple.Create(sqrt, x) };
                }), Keep.Both)
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());

            var source = t.Item1.Item1;
            var killSwitch = t.Item1.Item2;
            var sink = t.Item2;

            sink.Request(99);
            source.SendNext(1);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            source.SendNext(2);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            killSwitch.Abort(FailedElement.Exception);
            sink.ExpectError().InnerException.Should().Be(FailedElement.Exception);
        }

        [Fact]
        public void RetryConcat_tolerate_killswitch_terminations_inside_the_flow_on_start()
        {
            var t = this.SourceProbe<int>()
                .ViaMaterialized(KillSwitches.Single<int>(), Keep.Right)
                .Select(i => Tuple.Create(i, i))
                .Via(Retry.Concat(100, RetryFlow<int>(), x => new[] { Tuple.Create(x, x + 1) }))
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());

            var killSwitch = t.Item1;
            var sink = t.Item2;

            sink.Request(99);
            killSwitch.Abort(FailedElement.Exception);
            sink.ExpectError().InnerException.Should().Be(FailedElement.Exception);
        }

        [Fact]
        public void RetryConcat_tolerate_killswitch_terminations_inside_the_flow_before_start()
        {
            var innerFlow = RetryFlow<int>().ViaMaterialized(KillSwitches.Single<Tuple<Result<int>, int>>(), Keep.Right);

            var t = this.SourceProbe<int>()
                .ViaMaterialized(KillSwitches.Single<int>(), Keep.Right)
                .Select(i => Tuple.Create(i, i))
                .Via(Retry.Concat(100, innerFlow, x => new[] { Tuple.Create(x, x + 1) }))
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());

            var killSwitch = t.Item1;
            var sink = t.Item2;

            killSwitch.Abort(FailedElement.Exception);
            sink.Request(1);
            sink.ExpectError().InnerException.Should().Be(FailedElement.Exception);
        }
    }
}
