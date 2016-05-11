//-----------------------------------------------------------------------
// <copyright file="FramingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text; 
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.TestKit.Tests;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FramingSpec : AkkaSpec
    {
        private readonly ITestOutputHelper _helper;
        private ActorMaterializer Materializer { get; }

        public FramingSpec(ITestOutputHelper helper) : base(helper)
        {
            _helper = helper;
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private sealed class Rechunker : PushPullStage<ByteString, ByteString>
        {
            private ByteString _rechunkBuffer = ByteString.Empty;

            public override ISyncDirective OnPush(ByteString element, IContext<ByteString> context)
            {
                _rechunkBuffer += element;
                return Rechunk(context);
            }

            public override ISyncDirective OnPull(IContext<ByteString> context) => Rechunk(context);

            public override ITerminationDirective OnUpstreamFinish(IContext<ByteString> context)
                => _rechunkBuffer.IsEmpty ? context.Finish() : context.AbsorbTermination();

            private ISyncDirective Rechunk(IContext<ByteString> context)
            {
                if (!context.IsFinishing && ThreadLocalRandom.Current.Next(1, 3) == 2)
                    return context.Pull();

                var nextChunkSize = _rechunkBuffer.IsEmpty
                    ? 0
                    : ThreadLocalRandom.Current.Next(0, _rechunkBuffer.Count + 1);
                var newChunk = _rechunkBuffer.Take(nextChunkSize);
                _rechunkBuffer = _rechunkBuffer.Drop(nextChunkSize);
                return context.IsFinishing && _rechunkBuffer.IsEmpty
                    ? context.PushAndFinish(newChunk)
                    : context.Push(newChunk);
            }
        }

        private Flow<ByteString, ByteString, NotUsed> Rechunk
            => Flow.FromGraph(Flow.Create<ByteString>().Transform(() => new Rechunker()).Named("rechunker"));

        private static readonly List<ByteString> DelimiterBytes =
            new List<string> {"\n", "\r\n", "FOO"}.Select(ByteString.FromString).ToList();

        private static readonly List<ByteString> BaseTestSequences =
            new List<string> { "", "foo", "hello world" }.Select(ByteString.FromString).ToList();

        private static Flow<ByteString, string, NotUsed> SimpleLines(string delimiter, int maximumBytes, bool allowTruncation = true)
        {
            return  Flow.FromGraph(Framing.Delimiter(ByteString.FromString(delimiter), maximumBytes, allowTruncation)
                .Select(x => x.DecodeString(Encoding.UTF8)).Named("LineFraming"));
        }

        private static IEnumerable<ByteString> CompleteTestSequence(ByteString delimiter)
        {
            for (var i = 0; i < delimiter.Count; i++)
                foreach (var sequence in BaseTestSequences)
                    yield return delimiter.Take(i) + sequence;
        }

        [Fact]
        public void Delimiter_bytes_based_framing_must_work_with_various_delimiters_and_test_sequences()
        {
            for (var i = 1; i <= 100; i++)
            {
                foreach (var delimiter in DelimiterBytes)
                {
                    var task = Source.From(CompleteTestSequence(delimiter))
                        .Select(x => x + delimiter)
                        .Via(Rechunk)
                        .Via(Framing.Delimiter(delimiter, 256))
                        .Grouped(1000)
                        .RunWith(Sink.First<IEnumerable<ByteString>>(), Materializer);

                    task.Wait(TimeSpan.FromDays(3)).Should().BeTrue();
                    task.Result.ShouldAllBeEquivalentTo(CompleteTestSequence(delimiter));
                }
            }
        }

        [Fact]
        public void Delimiter_bytes_based_framing_must_respect_maximum_line_settings()
        {
            var task1 = Source.Single(ByteString.FromString("a\nb\nc\nd\n"))
                .Via(SimpleLines("\n", 1))
                .Limit(100)
                .RunWith(Sink.Seq<string>(), Materializer);

            task1.Wait(TimeSpan.FromDays(3)).Should().BeTrue();
            task1.Result.ShouldAllBeEquivalentTo(new[] {"a", "b", "c", "d"});

            var task2 =
                Source.Single(ByteString.FromString("ab\n"))
                    .Via(SimpleLines("\n", 1))
                    .Limit(100)
                    .RunWith(Sink.Seq<string>(), Materializer);
            task2.Invoking(t => t.Wait(TimeSpan.FromSeconds(3))).ShouldThrow<Framing.FramingException>();
        }

        [Fact]
        public void Delimiter_bytes_based_framing_must_work_with_empty_streams()
        {
            var task = Source.Empty<ByteString>().Via(SimpleLines("\n", 256)).RunAggregate(new List<string>(), (list, s) =>
            {
                list.Add(s);
                return list;
            }, Materializer);
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.Should().BeEmpty();
        }

        [Fact]
        public void Delimiter_bytes_based_framing_must_report_truncated_frames()
        {
            var task =
                Source.Single(ByteString.FromString("I habe no end"))
                    .Via(SimpleLines("\n", 256, false))
                    .Grouped(1000)
                    .RunWith(Sink.First<IEnumerable<string>>(), Materializer);

            task.Invoking(t => t.Wait(TimeSpan.FromSeconds(3))).ShouldThrow<Framing.FramingException>();
        }

        [Fact]
        public void Delimiter_bytes_based_framing_must_allow_truncated_frames_if_configured_so()
        {
            var task =
                Source.Single(ByteString.FromString("I have no end"))
                    .Via(SimpleLines("\n", 256))
                    .Grouped(1000)
                    .RunWith(Sink.First<IEnumerable<string>>(), Materializer);

            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.Should().ContainSingle(s => s.Equals("I have no end"));
        }

        private static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private static readonly ByteString ReferenceChunk = ByteString.FromString(RandomString(0x100001));

        private static readonly List<ByteOrder> ByteOrders = new List<ByteOrder>
        {
            ByteOrder.BigEndian,
            ByteOrder.LittleEndian
        };

        private static readonly List<int> FrameLengths = new List<int>
        {
            0,
            1,
            2,
            3,
            0xFF,
            0x100,
            0x101,
            0xFFF,
            0x1000,
            0x1001,
            0xFFFF,
            0x10000,
            0x10001
        };

        private static readonly List<int> FieldLengths = new List<int> {1, 2, 3, 4};

        private static readonly List<int> FieldOffsets = new List<int> {0, 1, 2, 3, 15, 16, 31, 32, 44, 107};

        private static ByteString Encode(ByteString payload, int fieldOffset, int fieldLength, ByteOrder byteOrder)
        {
            var h = new ByteStringBuilder().PutInt(payload.Count, byteOrder).Result();
            var header = byteOrder == ByteOrder.LittleEndian ? h.Take(fieldLength) : h.Drop(4 - fieldLength);

            return ByteString.Create(new byte[fieldOffset]) + header + payload;
        }

        [Fact]
        public void Length_field_based_framing_must_work_with_various_byte_orders_frame_lengths_and_offsets()
        {
            var counter = 1;
            foreach (var byteOrder in ByteOrders)
            {
                foreach (var fieldOffset in FieldOffsets)
                {
                    foreach (var fieldLength in FieldLengths)
                    {
                        var encodedFrames = FrameLengths.Where(x => x < 1L << (fieldLength * 8)).Select(length =>
                          {
                              var payload = ReferenceChunk.Take(length);
                              return Encode(payload, fieldOffset, fieldLength, byteOrder);
                          }).ToList();

                        var task = Source.From(encodedFrames)
                            .Via(Rechunk)
                            .Via(Framing.LengthField(fieldLength, int.MaxValue, fieldOffset, byteOrder))
                            .Grouped(10000)
                            .RunWith(Sink.First<IEnumerable<ByteString>>(), Materializer);

                        task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                        task.Result.ShouldAllBeEquivalentTo(encodedFrames);

                        _helper.WriteLine($"{counter++} from 80 passed");
                    }
                }
            }
        }

        [Fact]
        public void Length_field_based_framing_must_work_with_empty_streams()
        {
            var task = Source.Empty<ByteString>()
                .Via(Framing.LengthField(4, int.MaxValue, 0, ByteOrder.BigEndian))
                .RunAggregate(new List<ByteString>(), (list, s) =>
                {
                    list.Add(s);
                    return list;
                }, Materializer);

            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.Should().BeEmpty();
        }

        [Fact]
        public void Length_field_based_framing_must_report_oversized_frames()
        {
            var task1 = Source.Single(Encode(ReferenceChunk.Take(100), 0, 1, ByteOrder.BigEndian))
                .Via(Framing.LengthField(1, 99, 0, ByteOrder.BigEndian))
                .RunAggregate(new List<ByteString>(), (list, s) =>
                {
                    list.Add(s);
                    return list;
                }, Materializer);
            task1.Invoking(t => t.Wait(TimeSpan.FromSeconds(3))).ShouldThrow<Framing.FramingException>();

            var task2 = Source.Single(Encode(ReferenceChunk.Take(100), 49, 1, ByteOrder.BigEndian))
                .Via(Framing.LengthField(1, 100, 0, ByteOrder.BigEndian))
                .RunAggregate(new List<ByteString>(), (list, s) =>
                {
                    list.Add(s);
                    return list;
                }, Materializer);
            task2.Invoking(t => t.Wait(TimeSpan.FromSeconds(3))).ShouldThrow<Framing.FramingException>();
        }

        [Fact]
        public void Length_field_based_framing_must_report_truncated_frames()
        {
            foreach (var byteOrder in ByteOrders)
            {
                foreach (var fieldOffset in FieldOffsets)
                {
                    foreach (var fieldLength in FieldLengths)
                    {
                        foreach (var frameLength in FrameLengths.Where(f => f < 1 << (fieldLength * 8) && f != 0))
                        {
                            var fullFrame = Encode(ReferenceChunk.Take(frameLength), fieldOffset, fieldLength, byteOrder);
                            var partialFrame = fullFrame.DropRight(1);

                            Action action = () =>
                            {
                                    Source.From(new[] {fullFrame, partialFrame})
                                        .Via(Rechunk)
                                        .Via(Framing.LengthField(fieldLength, int.MaxValue, fieldOffset, byteOrder))
                                        .Grouped(10000)
                                        .RunWith(Sink.First<IEnumerable<ByteString>>(), Materializer)
                                        .Wait(TimeSpan.FromSeconds(5));
                            };
                            action.ShouldThrow<Framing.FramingException>();
                        }
                    }
                }
            }
        }

        [Fact]
        public void Length_field_based_framing_must_support_simple_framing_adapter()
        {
            var rechunkBidi = BidiFlow.FromFlowsMat(Rechunk, Rechunk, Keep.Left);
            var codecFlow = Framing.SimpleFramingProtocol(1024)
                .Atop(rechunkBidi)
                .Atop(Framing.SimpleFramingProtocol(1024).Reversed())
                .Join(Flow.Create<ByteString>()); // Loopback

            var random= new Random();
            var testMessages = Enumerable.Range(1, 100).Select(_ => ReferenceChunk.Take(random.Next(1024))).ToList();

            var task = Source.From(testMessages)
                .Via(codecFlow)
                .Limit(1000)
                .RunWith(Sink.Seq<ByteString>(), Materializer);

            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(testMessages);
        }
    }
}
