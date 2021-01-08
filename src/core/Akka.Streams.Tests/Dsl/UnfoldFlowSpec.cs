//-----------------------------------------------------------------------
// <copyright file="UnfoldFlowSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.Util;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class UnfoldFlowSpec
    {
        private static readonly TimeSpan _timeout = TimeSpan.FromMilliseconds(300);
        private static readonly int[] _outputs = new[]
        {
            27, 82, 41, 124, 62, 31, 94, 47, 142, 71, 214, 107, 322, 161, 484, 242, 121, 364, 182, 91, 274, 137,
            412, 206, 103, 310, 155, 466, 233, 700, 350, 175, 526, 263, 790, 395, 1186, 593, 1780, 890, 445, 1336,
            668, 334, 167, 502, 251, 754, 377, 1132, 566, 283, 850, 425, 1276, 638, 319, 958, 479, 1438, 719, 2158,
            1079, 3238, 1619, 4858, 2429, 7288, 3644, 1822, 911, 2734, 1367, 4102, 2051, 6154, 3077, 9232, 4616,
            2308, 1154, 577, 1732, 866, 433, 1300, 650, 325, 976, 488, 244, 122, 61, 184, 92, 46, 23, 70, 35, 106,
            53, 160, 80, 40, 20, 10, 5, 16, 8, 4, 2
        };

        public class WithSimpleFlow : Akka.TestKit.Xunit2.TestKit
        {
            private readonly Exception _done = new Exception("done");
            private readonly Source<int, (TestSubscriber.Probe<int>, TestPublisher.Probe<(int, int)>)> _source;

            public WithSimpleFlow()
            {
                var controlledFlow = Flow.FromSinkAndSource(this.SinkProbe<int>(), this.SourceProbe<(int, int)>(), Keep.Both);
                _source = SourceGen.UnfoldFlow(1, controlledFlow, _timeout);
            }

            [Fact]
            public void UnfoldFlow_should_unfold_Collatz_conjecture_with_a_sequence_of_111_elements_with_flow()
            {
                (int, int) Map(int x)
                {
                    if (x == 1)
                        throw _done;

                    if (x % 2 == 0)
                        return (x / 2, x);

                    return (x * 3 + 1, x);
                };

                var source = SourceGen.UnfoldFlow(27, 
                    Flow.FromFunction<int, (int, int)>(Map)
                    .Recover(ex =>
                    {
                        if (ex == _done)
                            return new Option<(int, int)>((1, 1));

                        return Option<(int, int)>.None;
                    }), 
                    _timeout);

                var sink = source.RunWith(this.SinkProbe<int>(), Sys.Materializer());

                foreach (var output in _outputs)
                {
                    sink.Request(1);
                    sink.ExpectNext(output);
                }

                sink.Request(1);
                sink.ExpectNext(1);
                sink.ExpectComplete();
            }

            [Fact]
            public void UnfoldFlow_should_unfold_Collatz_conjecture_with_a_sequence_of_111_elements_with_buffered_flow()
            {
                (int, int) Map(int x)
                {
                    if (x == 1)
                        throw _done;

                    if (x % 2 == 0)
                        return (x / 2, x);

                    return (x * 3 + 1, x);
                };

                Source<int, NotUsed> BufferedSource(int buffSize)
                {
                    return
                        SourceGen.UnfoldFlow(27,
                            Flow.FromFunction<int, (int, int)>(Map)
                            .Recover(ex =>
                            {
                                if (ex == _done)
                                    return new Option<(int, int)>((1, 1));

                                return Option<(int, int)>.None;
                            }), _timeout)
                        .Buffer(buffSize, OverflowStrategy.Backpressure);
                }

                var sink = BufferedSource(10).RunWith(this.SinkProbe<int>(), Sys.Materializer());

                sink.Request(_outputs.Length);
                foreach (var output in _outputs)
                    sink.ExpectNext(output);

                sink.Request(1);
                sink.ExpectNext(1);
                sink.ExpectComplete();
            }

            [Fact]
            public void UnfoldFlow_should_increment_integers_and_handle_KillSwitch_and_fail_instantly_when_aborted()
            {
                var t = _source.ToMaterialized(this.SinkProbe<int>(), Keep.Both).Run(Sys.Materializer());

                var sub = t.Item1.Item1;
                var pub = t.Item1.Item2;
                var snk = t.Item2;

                var kill = new Exception("KILL!");
                sub.EnsureSubscription();
                pub.EnsureSubscription();
                snk.EnsureSubscription();
                pub.SendError(kill);
                snk.ExpectError().Should().Be(kill);
            }

            [Fact(Skip ="Racy")]
            public void UnfoldFlow_should_increment_integers_and_handle_KillSwitch_and_fail_after_timeout_when_aborted()
            {
                var t = _source.ToMaterialized(this.SinkProbe<int>(), Keep.Both).Run(Sys.Materializer());

                var sub = t.Item1.Item1;
                var pub = t.Item1.Item2;
                var snk = t.Item2;

                sub.EnsureSubscription();
                pub.EnsureSubscription();
                snk.EnsureSubscription();
                sub.Cancel();
                snk.ExpectNoMsg(_timeout - TimeSpan.FromMilliseconds(50));
                snk.ExpectError();
            }

            [Fact]
            public void UnfoldFlow_should_increment_integers_and_handle_KillSwitch_and_fail_when_inner_stream_is_canceled_and_pulled_before_completion()
            {
                var t = _source.ToMaterialized(this.SinkProbe<int>(), Keep.Both).Run(Sys.Materializer());

                var sub = t.Item1.Item1;
                var pub = t.Item1.Item2;
                var snk = t.Item2;

                sub.EnsureSubscription();
                pub.EnsureSubscription();
                snk.EnsureSubscription();
                sub.Cancel();
                snk.Request(1);
                snk.ExpectNoMsg(_timeout - TimeSpan.FromMilliseconds(50));
                snk.ExpectError();
            }

            [Fact]
            public void UnfoldFlow_should_increment_integers_and_handle_KillSwitch_and_fail_when_inner_stream_is_canceled_pulled_before_completion_and_finally_aborted()
            {
                var t = _source.ToMaterialized(this.SinkProbe<int>(), Keep.Both).Run(Sys.Materializer());

                var sub = t.Item1.Item1;
                var pub = t.Item1.Item2;
                var snk = t.Item2;

                var kill = new Exception("KILL!");
                sub.EnsureSubscription();
                pub.EnsureSubscription();
                snk.EnsureSubscription();
                sub.Cancel();
                snk.Request(1);
                pub.SendError(kill);
                snk.ExpectError().Should().Be(kill);
            }

            [Fact]
            public void UnfoldFlow_should_increment_integers_and_handle_KillSwitch_and_fail_after_3_elements_when_aborted()
            {
                var t = _source.ToMaterialized(this.SinkProbe<int>(), Keep.Both).Run(Sys.Materializer());

                var sub = t.Item1.Item1;
                var pub = t.Item1.Item2;
                var snk = t.Item2;

                var kill = new Exception("KILL!");

                sub.EnsureSubscription();
                pub.EnsureSubscription();
                snk.EnsureSubscription();
                snk.Request(1);
                sub.RequestNext(1);
                pub.SendNext((2, 1));
                snk.ExpectNext(1);
                snk.Request(1);
                sub.RequestNext(2);
                pub.SendNext((3, 2));
                snk.ExpectNext(2);
                snk.Request(1);
                sub.RequestNext(3);
                pub.SendNext((4, 3));
                snk.ExpectNext(3);
                pub.SendError(kill);
                snk.ExpectError().Should().Be(kill);
            }

            [Fact]
            public void UnfoldFlow_should_increment_integers_and_handle_KillSwitch_and_complete_gracefully_instantly_when_stopped()
            {
                var t = _source.ToMaterialized(this.SinkProbe<int>(), Keep.Both).Run(Sys.Materializer());

                var sub = t.Item1.Item1;
                var pub = t.Item1.Item2;
                var snk = t.Item2;

                sub.EnsureSubscription();
                pub.EnsureSubscription();
                snk.EnsureSubscription();
                pub.SendComplete();
                snk.ExpectComplete();
            }

            [Fact(Skip ="Racy")]
            public void UnfoldFlow_should_increment_integers_and_handle_KillSwitch_and_complete_gracefully_after_timeout_when_stopped()
            {
                var t = _source.ToMaterialized(this.SinkProbe<int>(), Keep.Both).Run(Sys.Materializer());

                var sub = t.Item1.Item1;
                var pub = t.Item1.Item2;
                var snk = t.Item2;

                sub.EnsureSubscription();
                pub.EnsureSubscription();
                snk.EnsureSubscription();
                sub.Cancel();
                snk.ExpectNoMsg(_timeout - TimeSpan.FromMilliseconds(50));
                pub.SendComplete();
                snk.ExpectComplete();
            }

            [Fact]
            public void UnfoldFlow_should_increment_integers_and_handle_KillSwitch_and_complete_gracefully_after_3_elements_when_stopped()
            {
                var t = _source.ToMaterialized(this.SinkProbe<int>(), Keep.Both).Run(Sys.Materializer());

                var sub = t.Item1.Item1;
                var pub = t.Item1.Item2;
                var snk = t.Item2;

                sub.EnsureSubscription();
                pub.EnsureSubscription();
                snk.EnsureSubscription();
                snk.Request(1);
                sub.RequestNext(1);
                pub.SendNext((2, 1));
                snk.ExpectNext(1);
                snk.Request(1);
                sub.RequestNext(2);
                pub.SendNext((3, 2));
                snk.ExpectNext(2);
                snk.Request(1);
                sub.RequestNext(3);
                pub.SendNext((4, 3));
                snk.ExpectNext(3);
                pub.SendComplete();
                snk.ExpectComplete();
            }
        }

        public class WithFunction : Akka.TestKit.Xunit2.TestKit
        {
            private readonly Source<int, (TestSubscriber.Probe<int>, TestPublisher.Probe<int>)> _source;

            public WithFunction()
            {
                var controlledFlow = Flow.FromSinkAndSource(this.SinkProbe<int>(), this.SourceProbe<int>(), Keep.Both);
                _source = SourceGen.UnfoldFlowWith(1, controlledFlow, n => new Option<(int, int)>((n + 1, n)), _timeout);
            }

            [Fact]
            public void UnfoldFlow_should_unfold_Collatz_conjecture_with_a_sequence_of_111_elements_with_function()
            {
                Option<(int, int)> Map(int x)
                {
                    if (x == 1)
                        return Option<(int, int)>.None;

                    if (x % 2 == 0)
                        return new Option<(int, int)>((x / 2, x));

                    return new Option<(int, int)>((x * 3 + 1, x));
                }

                var source = SourceGen.UnfoldFlowWith(27, Flow.FromFunction<int, int>(x => x), Map, _timeout);
                var sink = source.RunWith(this.SinkProbe<int>(), Sys.Materializer());
                foreach (var output in _outputs)
                {
                    sink.Request(1);
                    sink.ExpectNext(output);
                }
                sink.Request(1);
                sink.ExpectComplete();
            }

            [Fact]
            public void UnfoldFlow_should_increment_integers_and_handle_KillSwitch_and_fail_instantly_when_aborted()
            {
                var t = _source.ToMaterialized(this.SinkProbe<int>(), Keep.Both).Run(Sys.Materializer());

                var sub = t.Item1.Item1;
                var pub = t.Item1.Item2;
                var snk = t.Item2;

                var kill = new Exception("KILL!");
                sub.EnsureSubscription();
                pub.EnsureSubscription();
                snk.EnsureSubscription();
                pub.SendError(kill);
                snk.ExpectError().Should().Be(kill);
            }

            [Fact(Skip ="Racy")]
            public void UnfoldFlow_should_increment_integers_and_handle_KillSwitch_and_fail_after_timeout_when_aborted()
            {
                var t = _source.ToMaterialized(this.SinkProbe<int>(), Keep.Both).Run(Sys.Materializer());

                var sub = t.Item1.Item1;
                var pub = t.Item1.Item2;
                var snk = t.Item2;

                sub.EnsureSubscription();
                pub.EnsureSubscription();
                snk.EnsureSubscription();
                sub.Cancel();
                snk.ExpectNoMsg(_timeout - TimeSpan.FromMilliseconds(50));
                snk.ExpectError();
            }

            [Fact]
            public void UnfoldFlow_should_increment_integers_and_handle_KillSwitch_and_fail_when_inner_stream_is_canceled_and_pulled_before_completion()
            {
                var t = _source.ToMaterialized(this.SinkProbe<int>(), Keep.Both).Run(Sys.Materializer());

                var sub = t.Item1.Item1;
                var pub = t.Item1.Item2;
                var snk = t.Item2;

                sub.EnsureSubscription();
                pub.EnsureSubscription();
                snk.EnsureSubscription();
                sub.Cancel();
                snk.Request(1);
                snk.ExpectNoMsg(_timeout - TimeSpan.FromMilliseconds(50));
                snk.ExpectError();
            }

            [Fact]
            public void UnfoldFlow_should_increment_integers_and_handle_KillSwitch_and_fail_when_inner_stream_is_canceled_pulled_before_completion_and_finally_aborted()
            {
                var t = _source.ToMaterialized(this.SinkProbe<int>(), Keep.Both).Run(Sys.Materializer());

                var sub = t.Item1.Item1;
                var pub = t.Item1.Item2;
                var snk = t.Item2;

                var kill = new Exception("KILL!");
                sub.EnsureSubscription();
                pub.EnsureSubscription();
                snk.EnsureSubscription();
                sub.Cancel();
                snk.Request(1);
                pub.SendError(kill);
                snk.ExpectError().Should().Be(kill);
            }

            [Fact]
            public void UnfoldFlow_should_increment_integers_and_handle_KillSwitch_and_fail_after_3_elements_when_aborted()
            {
                var t = _source.ToMaterialized(this.SinkProbe<int>(), Keep.Both).Run(Sys.Materializer());

                var sub = t.Item1.Item1;
                var pub = t.Item1.Item2;
                var snk = t.Item2;

                var kill = new Exception("KILL!");
                sub.EnsureSubscription();
                pub.EnsureSubscription();
                snk.EnsureSubscription();
                snk.Request(1);
                sub.RequestNext(1);
                pub.SendNext(1);
                snk.ExpectNext(1);
                snk.Request(1);
                sub.RequestNext(2);
                pub.SendNext(2);
                snk.ExpectNext(2);
                snk.Request(1);
                sub.RequestNext(3);
                pub.SendNext(3);
                snk.ExpectNext(3);
                pub.SendError(kill);
                snk.ExpectError().Should().Be(kill);
            }

            [Fact]
            public void UnfoldFlow_should_increment_integers_and_handle_KillSwitch_and_complete_gracefully_instantly_when_stopped()
            {
                var t = _source.ToMaterialized(this.SinkProbe<int>(), Keep.Both).Run(Sys.Materializer());

                var sub = t.Item1.Item1;
                var pub = t.Item1.Item2;
                var snk = t.Item2;

                sub.EnsureSubscription();
                pub.EnsureSubscription();
                snk.EnsureSubscription();
                pub.SendComplete();
                snk.ExpectComplete();
            }

            [Fact]
            public void UnfoldFlow_should_increment_integers_and_handle_KillSwitch_and_complete_gracefully_after_timeout_when_stopped()
            {
                var t = _source.ToMaterialized(this.SinkProbe<int>(), Keep.Both).Run(Sys.Materializer());

                var sub = t.Item1.Item1;
                var pub = t.Item1.Item2;
                var snk = t.Item2;

                sub.EnsureSubscription();
                pub.EnsureSubscription();
                snk.EnsureSubscription();
                sub.Cancel();
                snk.ExpectNoMsg(_timeout - TimeSpan.FromMilliseconds(50));
                pub.SendComplete();
                snk.ExpectComplete();
            }

            [Fact]
            public void UnfoldFlow_should_increment_integers_and_handle_KillSwitch_and_complete_gracefully_after_3_elements_when_stopped()
            {
                var t = _source.ToMaterialized(this.SinkProbe<int>(), Keep.Both).Run(Sys.Materializer());

                var sub = t.Item1.Item1;
                var pub = t.Item1.Item2;
                var snk = t.Item2;

                sub.EnsureSubscription();
                pub.EnsureSubscription();
                snk.EnsureSubscription();
                snk.Request(1);
                sub.RequestNext(1);
                pub.SendNext(1);
                snk.ExpectNext(1);
                snk.Request(1);
                sub.RequestNext(2);
                pub.SendNext(2);
                snk.ExpectNext(2);
                snk.Request(1);
                sub.RequestNext(3);
                pub.SendNext(3);
                snk.ExpectNext(3);
                pub.SendComplete();
                snk.ExpectComplete();
            }
        }
    }
}
