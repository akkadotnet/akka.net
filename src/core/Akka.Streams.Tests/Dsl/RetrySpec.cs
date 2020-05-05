//-----------------------------------------------------------------------
// <copyright file="RetrySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.Util;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class RetrySpec : Akka.TestKit.Xunit2.TestKit
    {
        private static readonly Result<int> FailedElement = Result.Failure<int>(new Exception("cooked failure"));

        private static Flow<(int, T), (Result<int>, T), NotUsed> RetryFlow<T>() =>
            Flow.Identity<(int, T)>()
                .Select(t => t.Item1 % 2 == 0
                    ? (FailedElement, t.Item2)
                    : (Result.Success(t.Item1 + 1), t.Item2));

        [Fact]
        public void Retry_should_retry_ints_according_to_their_parity()
        {
            var t = this.SourceProbe<int>()
                .Select(i => (i, i))
                .Via(Retry.Create(RetryFlow<int>(), s => s < 42 ? (s + 1, s + 1) : Option<(int, int)>.None))
                .ToMaterialized(this.SinkProbe<(Result<int>, int)>(), Keep.Both)
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
                .Select(i => (i, Enumerable.Range(0, i).Reverse().Select(x => x * 2).Concat(new[] { i + 1 }).ToImmutableList()))
                .Via(Retry.Create(RetryFlow<ImmutableList<int>>(),
                    s => s.IsEmpty
                        ? throw new IllegalStateException("should not happen")
                        : (s[0], s.RemoveAt(0))))
                .ToMaterialized(this.SinkProbe<(Result<int>, ImmutableList<int>)>(), Keep.Both)
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
                .Select(i => (i, i * i))
                .Via(Retry.Create(RetryFlow<int>(), s =>
                {
                    if (s % 4 == 0) return (s / 2, s / 4);
                    var sqrt = (int)Math.Sqrt(s);
                    return (sqrt, s);
                }))
                .ToMaterialized(this.SinkProbe<(Result<int>, int)>(), Keep.Both)
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
                .Select(i => (i, i * i))
                .Via(Retry.Create(RetryFlow<int>(), x =>
                {
                    if (x % 4 == 0) return (x / 2, x / 4);
                    var sqrt = (int)Math.Sqrt(x);
                    return (sqrt, x);
                }))
                .ToMaterialized(this.SinkProbe<(Result<int>, int)>(), Keep.Both)
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
                .Select(i => (i, i))
                .Via(Retry.Create(RetryFlow<int>(), x => (x, x + 1)))
                .ToMaterialized(this.SinkProbe<(Result<int>, int)>(), Keep.Both)
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
                .Select(i => (i, i))
                .Via(Retry.Create(RetryFlow<int>(), x => (x, x + 1)))
                .ToMaterialized(this.SinkProbe<(Result<int>, int)>(), Keep.Both)
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
            var innerFlow = RetryFlow<int>().ViaMaterialized(KillSwitches.Single<(Result<int>, int)>(), Keep.Right);

            var t = this.SourceProbe<int>()
                .Select(i => (i, i * i))
                .ViaMaterialized(Retry.Create(innerFlow, x =>
                {
                    if (x % 4 == 0) return (x / 2, x / 4);
                    var sqrt = (int)Math.Sqrt(x);
                    return (sqrt, x);
                }), Keep.Both)
                .ToMaterialized(this.SinkProbe<(Result<int>, int)>(), Keep.Both)
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
            var innerFlow = RetryFlow<int>().ViaMaterialized(KillSwitches.Single<(Result<int>, int)>(), Keep.Right);

            var t = this.SourceProbe<int>()
                .Select(i => (i, i))
                .ViaMaterialized(Retry.Create(innerFlow, x => (x, x + 1)), Keep.Right)
                .ToMaterialized(this.SinkProbe<(Result<int>, int)>(), Keep.Both)
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
            var innerFlow = RetryFlow<int>().ViaMaterialized(KillSwitches.Single<(Result<int>, int)>(), Keep.Right);

            var t = this.SourceProbe<int>()
                .Select(i => (i, i))
                .ViaMaterialized(Retry.Create(innerFlow, x => (x, x + 1)), Keep.Right)
                .ToMaterialized(this.SinkProbe<(Result<int>, int)>(), Keep.Both)
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
                .Select(i => (i, i))
                .Via(Retry.Concat(100, RetryFlow<int>(), _ => Enumerable.Empty<(int, int)>()))
                .ToMaterialized(this.SinkProbe<(Result<int>, int)>(), Keep.Both)
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
                .Select(i => (i, i))
                .Via(Retry.Concat(100, RetryFlow<int>(), os =>
                {
                    var s = (os + 1) % 3;
                    if (os < 42) return new[] { (os + 1, os + 1), (s, s) };
                    if (os == 42) return new (int, int)[0];
                    return null;
                }))
                .ToMaterialized(this.SinkProbe<(Result<int>, int)>(), Keep.Both)
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
                .Select(i => (i, i * i))
                .Via(Retry.Concat(100, RetryFlow<int>(), x =>
                {
                    if (x % 4 == 0) return new[] { (x / 2, x / 4) };
                    var sqrt = (int)Math.Sqrt(x);
                    return new[] { (sqrt, x) };
                }))
                .ToMaterialized(this.SinkProbe<(Result<int>, int)>(), Keep.Both)
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
                .Select(i => (i, i * i))
                .Via(Retry.Concat(100, RetryFlow<int>(), x =>
                {
                    if (x % 4 == 0) return new[] { (x / 2, x / 4) };
                    var sqrt = (int)Math.Sqrt(x);
                    return new[] { (sqrt, x) };
                }))
                .ToMaterialized(this.SinkProbe<(Result<int>, int)>(), Keep.Both)
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
                .Select(i => (i, i))
                .Via(Retry.Concat(100, RetryFlow<int>(), x => new[] { (x, x + 1) }))
                .ToMaterialized(this.SinkProbe<(Result<int>, int)>(), Keep.Both)
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
                .Select(i => (i, i))
                .Via(Retry.Concat(100, RetryFlow<int>(), x => new[] { (x, x + 1) }))
                .ToMaterialized(this.SinkProbe<(Result<int>, int)>(), Keep.Both)
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
            var innerFlow = RetryFlow<int>().ViaMaterialized(KillSwitches.Single<(Result<int>, int)>(), Keep.Right);

            var t = this.SourceProbe<int>()
                .Select(i => (i, i * i))
                .ViaMaterialized(Retry.Concat(100, innerFlow, x =>
                {
                    if (x % 4 == 0) return new[] { (x / 2, x / 4) };
                    var sqrt = (int)Math.Sqrt(x);
                    return new[] { (sqrt, x) };
                }), Keep.Both)
                .ToMaterialized(this.SinkProbe<(Result<int>, int)>(), Keep.Both)
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
                .Select(i => (i, i))
                .Via(Retry.Concat(100, RetryFlow<int>(), x => new[] { (x, x + 1) }))
                .ToMaterialized(this.SinkProbe<(Result<int>, int)>(), Keep.Both)
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
            var innerFlow = RetryFlow<int>().ViaMaterialized(KillSwitches.Single<(Result<int>, int)>(), Keep.Right);

            var t = this.SourceProbe<int>()
                .ViaMaterialized(KillSwitches.Single<int>(), Keep.Right)
                .Select(i => (i, i))
                .Via(Retry.Concat(100, innerFlow, x => new[] { (x, x + 1) }))
                .ToMaterialized(this.SinkProbe<(Result<int>, int)>(), Keep.Both)
                .Run(Sys.Materializer());

            var killSwitch = t.Item1;
            var sink = t.Item2;

            killSwitch.Abort(FailedElement.Exception);
            sink.Request(1);
            sink.ExpectError().InnerException.Should().Be(FailedElement.Exception);
        }
    }
}
