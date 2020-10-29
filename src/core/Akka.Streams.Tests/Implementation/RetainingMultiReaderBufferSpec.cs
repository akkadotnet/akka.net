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
    public class RetainingMultiReaderBufferSpec
    {
        [Theory]
        [InlineData(2, 4, 1, "0 0 (size=0, cursors=1)")]
        [InlineData(4, 4, 3, "0 0 0 0 (size=0, cursors=3)")]
        public void A_RetainingMultiReaderBufferSpec_should_initially_be_empty(int iSize, int mSize, int cursorCount, string expected)
        {
            var test = new Test(iSize, mSize, cursorCount);
            test.Inspect().Should().Be(expected);
        }

        [Fact]
        public void A_RetainingMultiReaderBufferSpec_should_fail_reads_if_nothing_can_be_read()
        {
            var test = new Test(4, 4, 3);
            test.Write(1).Should().BeTrue();
            test.Write(2).Should().BeTrue();
            test.Write(3).Should().BeTrue();
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

        [Fact]
        public void A_RetainingMultiReaderBufferSpec_should_automatically_grow_if_possible()
        {
            var test = new Test(2, 8, 2);
            test.Write(1).Should().BeTrue();
            test.Inspect().Should().Be("1 0 (size=1, cursors=2)");
            test.Write(2).Should().BeTrue();
            test.Inspect().Should().Be("1 2 (size=2, cursors=2)");
            test.Write(3).Should().BeTrue();
            test.Inspect().Should().Be("1 2 3 0 (size=3, cursors=2)");
            test.Write(4).Should().BeTrue();
            test.Inspect().Should().Be("1 2 3 4 (size=4, cursors=2)");
            test.Read(0).Should().Be(1);
            test.Read(0).Should().Be(2);
            test.Read(0).Should().Be(3);
            test.Read(1).Should().Be(1);
            test.Read(1).Should().Be(2);
            test.Write(5).Should().BeTrue();
            test.Inspect().Should().Be("1 2 3 4 5 0 0 0 (size=5, cursors=2)");
            test.Write(6).Should().BeTrue();
            test.Inspect().Should().Be("1 2 3 4 5 6 0 0 (size=6, cursors=2)");
            test.Write(7).Should().BeTrue();
            test.Inspect().Should().Be("1 2 3 4 5 6 7 0 (size=7, cursors=2)");
            test.Read(0).Should().Be(4);
            test.Read(0).Should().Be(5);
            test.Read(0).Should().Be(6);
            test.Read(0).Should().Be(7);
            test.Read(0).Should().Be(null);
            test.Read(1).Should().Be(3);
            test.Read(1).Should().Be(4);
            test.Read(1).Should().Be(5);
            test.Read(1).Should().Be(6);
            test.Read(1).Should().Be(7);
            test.Read(1).Should().Be(null);
            test.Inspect().Should().Be("1 2 3 4 5 6 7 0 (size=7, cursors=2)");
        }

        [Fact]
        public void A_RetainingMultiReaderBufferSpec_should_pass_the_stress_test()
        {
            // create 100 buffers with an initialSize of 1 and a maxSize of 1 to 64,
            // for each one attach 1 to 8 cursors and randomly try reading and writing to the buffer;
            // in total 200 elements need to be written to the buffer and read in the correct order by each cursor
            var MAXSIZEBIT_LIMIT = 6; // 2 ^ (this number)
            var COUNTER_LIMIT = 200;
            var LOG = false;
            var sb = new StringBuilder();
            var log = new Action<string>(s =>
            {
                if (LOG)
                    sb.Append(s);
            });

            var random = new Random();
            for (var bit = 1; bit <= MAXSIZEBIT_LIMIT; bit++)
                for (var n = 1; n <= 2; n++)
                {
                    var counter = 1;
                    var activeCoursors =
                        Enumerable.Range(0, random.Next(8) + 1)
                            .Select(i => new StressTestCursor(i, 1 << bit, log, COUNTER_LIMIT, sb))
                            .ToList();
                    var stillWriting = 2;// give writing a slight bias, so as to somewhat "stretch" the buffer
                    var buf = new TestBuffer(1, 1 << bit, new SimpleCursors(activeCoursors));
                    sb.Clear();

                    while (activeCoursors.Count != 0)
                    {
                        log($"Buf: {buf.Inspect()}\n");
                        var activeCursorCount = activeCoursors.Count;
                        var index = random.Next(activeCursorCount + stillWriting);
                        if (index >= activeCursorCount)
                        {
                            log($"  Writing {counter}: ");
                            if (buf.Write(counter))
                            {
                                log("OK\n");
                                counter++;
                            }
                            else
                            {
                                log("FAILED\n");
                                if (counter == COUNTER_LIMIT)
                                    stillWriting = 0;
                            }
                        }
                        else
                        {
                            var cursor = activeCoursors[index];
                            if (cursor.TryReadAndReturnTrueIfDone(buf))
                                activeCoursors = activeCoursors.Where(c => c != cursor).ToList();
                        }
                    }
                }
        }

        private class TestBuffer : RetainingMultiReaderBuffer<int?>
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
