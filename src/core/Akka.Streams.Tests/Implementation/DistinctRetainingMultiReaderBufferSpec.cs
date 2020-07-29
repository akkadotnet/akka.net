using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Streams.Implementation;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace Akka.Streams.Tests.Implementation
{
    public class DistinctRetainingMultiReaderBufferSpec
    {
        // The rest of the tests are covered by ResizableMultiReaderRingBufferSpec

        [Fact]
        public void A_DistinctRetainingMultiReaderBuffer_should_store_distinct_values_only()
        {
            var test = new Test(4, 4, 3);
            test.Write(1).Should().BeTrue();
            test.Write(2).Should().BeTrue();
            test.Write(3).Should().BeTrue();
            test.Write(2).Should().BeTrue();
            test.Write(2).Should().BeTrue();
            test.Write(1).Should().BeTrue();
            test.Inspect().Should().Be("1 2 3 0 (size=3, cursors=3)");
            test.Read(0).Should().Be(1);
            test.Read(0).Should().Be(2);
            test.Read(1).Should().Be(1);
            test.Inspect().Should().Be("1 2 3 0 (size=3, cursors=3)");
            test.Read(0).Should().Be(3);
            test.Read(0).Should().Be(null);
            test.Read(1).Should().Be(2);
            test.Inspect().Should().Be("1 2 3 0 (size=3, cursors=3)");
            test.Read(2).Should().Be(1);
            test.Inspect().Should().Be("1 2 3 0 (size=3, cursors=3)");
            test.Read(1).Should().Be(3);
            test.Read(1).Should().Be(null);
            test.Read(2).Should().Be(2);
            test.Read(2).Should().Be(3);
            test.Inspect().Should().Be("1 2 3 0 (size=3, cursors=3)");
        }

        private class TestBuffer : DistinctRetainingMultiReaderBuffer<int?>
        {
            public ICursors UnderlyingCursors { get; }

            public TestBuffer(int initialSize, int maxSize, ICursors cursors) : base(initialSize, maxSize, cursors)
            {
                UnderlyingCursors = cursors;
            }

            public string Inspect()
            {
                return Buffer.Select(x => x ?? 0).Aggregate("", (s, i) => s + i + " ") +
                ToString().SkipWhile(c => c != '(').Aggregate("", (s, c) => s + c);
            }
        }

        private class Test : TestBuffer
        {
            public Test(int initialSize, int maxSize, int cursorCount) : base(initialSize, maxSize, new SimpleCursors(cursorCount))
            {
            }

            public int? Read(int cursorIx)
            {
                try
                {
                    return Read(Cursors.Cursors.ElementAt(cursorIx));
                }
                catch (NothingToReadException)
                {
                    return null;
                }
            }
        }

        private class SimpleCursors : ICursors
        {
            public SimpleCursors(IEnumerable<ICursor> cursors)
            {
                Cursors = cursors;
            }

            public SimpleCursors(int cursorCount)
            {
                Cursors = Enumerable.Range(0, cursorCount).Select(_ => new SimpleCursor()).ToList();
            }

            public IEnumerable<ICursor> Cursors { get; }
        }

        private class SimpleCursor : ICursor
        {
            public long Cursor { get; set; }
        }

        private class StressTestCursor : ICursor
        {
            private readonly int _cursorNr;
            private readonly int _run;
            private readonly Action<string> _log;
            private readonly int _counterLimit;
            private readonly StringBuilder _sb;
            private int _counter = 1;

            public StressTestCursor(int cursorNr, int run, Action<string> log, int counterLimit, StringBuilder sb)
            {
                _cursorNr = cursorNr;
                _run = run;
                _log = log;
                _counterLimit = counterLimit;
                _sb = sb;
            }

            public bool TryReadAndReturnTrueIfDone(TestBuffer buf)
            {
                _log($"  Try reading of {this}: ");
                try
                {
                    var x = buf.Read(this);
                    _log("OK\n");
                    if (x != _counter)
                    {
                        throw new AssertionFailedException(
                            $@"|Run {_run}, cursorNr {_cursorNr}, counter {_counter}: got unexpected {x}
                         |  Buf: {buf.Inspect()}
                         |  Cursors: {buf.UnderlyingCursors.Cursors.Aggregate("           ", (s, cursor) => s + cursor + "\n           ")}
                         |Log: {_sb}
                      ");
                    }
                    _counter++;
                    return _counter == _counterLimit;
                }
                catch (NothingToReadException)
                {
                    _log("FAILED\n");
                    return false; // ok, we currently can't read, try again later
                }
            }

            public long Cursor { get; set; }

            public override string ToString() => $"cursorNr {_cursorNr}, ix {Cursor}, counter {_counter}";
        }
    }
}
