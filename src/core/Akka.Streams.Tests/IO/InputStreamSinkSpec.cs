//-----------------------------------------------------------------------
// <copyright file="InputStreamSinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.IO;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Xunit.Attributes;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.IO
{
    public class InputStreamSinkSpec : AkkaSpec
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(300);
        private readonly ActorMaterializer _materializer;
        private readonly ReadOnlyMemory<byte> _byteString = RandomByteString(3);

        public InputStreamSinkSpec(ITestOutputHelper helper) : base(Utils.UnboundedMailboxConfig, helper)
        {
            Sys.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
            var settings = ActorMaterializerSettings.Create(Sys).WithDispatcher("akka.actor.default-dispatcher");
            _materializer = Sys.Materializer(settings);
        }

        [Fact]
        public async Task InputStreamSink_should_read_bytes_from_input_stream()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var inputStream = Source.Single(_byteString).RunWith(StreamConverters.AsInputStream(), _materializer);
                var result = ReadN(inputStream, _byteString.Length);
                inputStream.Dispose();
                result.Item1.Should().Be(_byteString.Length);
                result.Item2.Span.SequenceEqual(_byteString.Span).Should().BeTrue();
                return Task.CompletedTask;
            }, _materializer);
        }

        [Fact]
        public async Task InputStreamSink_should_read_bytes_correctly_if_requested_by_input_stream_not_in_chunk_size()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var sinkProbe = CreateTestProbe();
                var byteString2 = RandomByteString(3);
                var inputStream = Source.From(new[] { _byteString, byteString2 })
                    .RunWith(TestSink(sinkProbe), _materializer);

                sinkProbe.ExpectMsgAllOf(new[] { GraphStageMessages.Push.Instance, GraphStageMessages.Push.Instance });

                var result = ReadN(inputStream, 2);
                result.Item1.Should().Be(2);
                result.Item2.Span.SequenceEqual(_byteString.Slice(0, 2).Span).Should().BeTrue();

                result = ReadN(inputStream, 2);
                result.Item1.Should().Be(2);
                // _byteString[2..] + byteString2[0..1]
                var expected2 = new byte[2];
                _byteString.Slice(2).Span.CopyTo(expected2);
                byteString2.Slice(0, 1).Span.CopyTo(expected2.AsSpan(1));
                result.Item2.Span.SequenceEqual(expected2).Should().BeTrue();

                result = ReadN(inputStream, 2);
                result.Item1.Should().Be(2);
                result.Item2.Span.SequenceEqual(byteString2.Slice(1).Span).Should().BeTrue();

                inputStream.Dispose();
                return Task.CompletedTask;
            }, _materializer);
        }

        [Fact]
        public async Task InputStreamSink_should_return_less_than_was_expected_when_data_source_has_provided_some_but_not_enough_data()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var inputStream = Source.Single(_byteString).RunWith(StreamConverters.AsInputStream(), _materializer);

                var arr = new byte[_byteString.Length + 1];
// CA2022 - testing our own Stream.Read override in InputStreamAdapter
#pragma warning disable CA2022
                inputStream.Read(arr, 0, arr.Length).Should().Be(arr.Length - 1);
#pragma warning restore CA2022
                inputStream.Dispose();
                // arr should contain _byteString bytes followed by a zero byte
                arr.AsSpan(0, _byteString.Length).SequenceEqual(_byteString.Span).Should().BeTrue();
                arr[_byteString.Length].Should().Be(0);
                return Task.CompletedTask;
            }, _materializer);
        }

        [WindowsFact(Skip ="Racy in Linux")]
        public async Task InputStreamSink_should_block_read_until_get_requested_number_of_bytes_from_upstream()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var run =                                                                             
                this.SourceProbe<ReadOnlyMemory<byte>>()                                                                                 
                .ToMaterialized(StreamConverters.AsInputStream(), Keep.Both)                                                                                 
                .Run(_materializer);
                var probe = run.Item1;
                var inputStream = run.Item2;
// CA2022 - testing our own Stream.Read override in InputStreamAdapter
#pragma warning disable CA2022
                var f = Task.Run(() => inputStream.Read(new byte[_byteString.Length], 0, _byteString.Length));
#pragma warning restore CA2022

                f.Wait(Timeout).Should().BeFalse();

                probe.SendNext(_byteString);
                f.Wait(RemainingOrDefault).Should().BeTrue();
                f.Result.Should().Be(_byteString.Length);

                probe.SendComplete();
                inputStream.ReadByte().Should().Be(-1);
                inputStream.Dispose();
                return Task.CompletedTask;
            }, _materializer);
        }

        [Fact]
        public async Task InputStreamSink_should_throw_error_when_reactive_stream_is_closed()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var t = this.SourceProbe<ReadOnlyMemory<byte>>()
                .ToMaterialized(StreamConverters.AsInputStream(), Keep.Both)
                .Run(_materializer);
                var probe = t.Item1;
                var inputStream = t.Item2;

                probe.SendNext(_byteString);
                inputStream.Dispose();
                probe.ExpectCancellation();

// CA2022 - testing our own Stream.Read override in InputStreamAdapter
#pragma warning disable CA2022
                Action block = () => inputStream.Read(new byte[1], 0, 1);
#pragma warning restore CA2022
                block.Should().Throw<IOException>();
                return Task.CompletedTask;
            }, _materializer);
        }

        [Fact]
        public async Task InputStreamSink_should_return_all_data_when_upstream_is_completed()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var sinkProbe = CreateTestProbe();
                var t = this.SourceProbe<ReadOnlyMemory<byte>>().ToMaterialized(TestSink(sinkProbe), Keep.Both).Run(_materializer);
                var probe = t.Item1;
                var inputStream = t.Item2;
                var bytes = RandomByteString(1);

                probe.SendNext(bytes);
                sinkProbe.ExpectMsg<GraphStageMessages.Push>();

                probe.SendComplete();
                sinkProbe.ExpectMsg<GraphStageMessages.UpstreamFinish>();

                var result = ReadN(inputStream, 3);
                result.Item1.Should().Be(1);
                result.Item2.Span.SequenceEqual(bytes.Span).Should().BeTrue();
                return Task.CompletedTask;
            }, _materializer);
        }

        [Fact]
        public async Task InputStreamSink_should_work_when_read_chunks_smaller_then_stream_chunks()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var bytes = RandomByteString(10);
                var inputStream = Source.Single(bytes).RunWith(StreamConverters.AsInputStream(), _materializer);

                while (!bytes.IsEmpty)
                {
                    var max = Math.Min(bytes.Length, 3);
                    var expected = bytes.Slice(0, max);
                    bytes = bytes.Slice(max);

                    var result = ReadN(inputStream, max);
                    result.Item1.Should().Be(expected.Length);
                    result.Item2.Span.SequenceEqual(expected.Span).Should().BeTrue();
                }

                inputStream.Dispose();
                return Task.CompletedTask;
            }, _materializer);
        }

        [Fact]
        public async Task InputStreamSink_should_throw_exception_when_call_read_With_wrong_parameters()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var inputStream = Source.Single(_byteString).RunWith(StreamConverters.AsInputStream(), _materializer);
                var buf = new byte[3];

// CA2022 - testing our own Stream.Read override in InputStreamAdapter
#pragma warning disable CA2022
                Action(() => inputStream.Read(buf, -1, 2)).Should().Throw<ArgumentException>();
                Action(() => inputStream.Read(buf, 0, 5)).Should().Throw<ArgumentException>();
                Action(() => inputStream.Read(Array.Empty<byte>(), 0, 1)).Should().Throw<ArgumentException>();
                Action(() => inputStream.Read(buf, 0, 0)).Should().Throw<ArgumentException>();
#pragma warning restore CA2022
                return Task.CompletedTask;
            }, _materializer);
        }

        private Action Action(Action a) => a;

        [Fact]
        public async Task InputStreamSink_should_successfully_read_several_chunks_at_once()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var bytes = Enumerable.Range(1, 4).Select(_ => RandomByteString(4)).ToList();
                var sinkProbe = CreateTestProbe();
                var inputStream = Source.From(bytes).RunWith(TestSink(sinkProbe), _materializer);

                //need to wait while all elements arrive to sink
                bytes.ForEach(_ => sinkProbe.ExpectMsg<GraphStageMessages.Push>());

                for (var i = 0; i < 2; i++)
                {
                    var r = ReadN(inputStream, 8);
                    r.Item1.Should().Be(8);
                    var combined = new byte[8];
                    bytes[i * 2].Span.CopyTo(combined);
                    bytes[i * 2 + 1].Span.CopyTo(combined.AsSpan(bytes[i * 2].Length));
                    r.Item2.Span.SequenceEqual(combined).Should().BeTrue();
                }

                inputStream.Dispose();
                return Task.CompletedTask;
            }, _materializer);
        }

        [Fact]
        public async Task InputStreamSink_should_work_when_read_chunks_bigger_than_stream_chunks()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var bytes1 = RandomByteString(10);
                var bytes2 = RandomByteString(10);
                var sinkProbe = CreateTestProbe();
                var inputStream = Source.From(new[] { bytes1, bytes2 }).RunWith(TestSink(sinkProbe), _materializer);

                //need to wait while both elements arrive to sink
                sinkProbe.ExpectMsgAllOf(new[] { GraphStageMessages.Push.Instance, GraphStageMessages.Push.Instance });

                var r1 = ReadN(inputStream, 15);
                r1.Item1.Should().Be(15);
                var expected1 = new byte[15];
                bytes1.Span.CopyTo(expected1);
                bytes2.Slice(0, 5).Span.CopyTo(expected1.AsSpan(10));
                r1.Item2.Span.SequenceEqual(expected1).Should().BeTrue();

                var r2 = ReadN(inputStream, 15);
                r2.Item1.Should().Be(5);
                r2.Item2.Span.SequenceEqual(bytes2.Slice(5).Span).Should().BeTrue();

                inputStream.Dispose();
                return Task.CompletedTask;
            }, _materializer);
        }

        [Fact]
        public async Task InputStreamSink_should_return_minus_1_when_read_after_stream_is_completed()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var inputStream = Source.Single(_byteString).RunWith(StreamConverters.AsInputStream(), _materializer);

                var r = ReadN(inputStream, _byteString.Length);
                r.Item1.Should().Be(_byteString.Length);
                r.Item2.Span.SequenceEqual(_byteString.Span).Should().BeTrue();

                inputStream.ReadByte().Should().Be(-1);
                inputStream.Dispose();
                return Task.CompletedTask;
            }, _materializer);
        }

        [Fact]
        public async Task InputStreamSink_should_return_Exception_when_stream_is_failed()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var sinkProbe = CreateTestProbe();
                var t = this.SourceProbe<ReadOnlyMemory<byte>>().ToMaterialized(TestSink(sinkProbe), Keep.Both).Run(_materializer);
                var probe = t.Item1;
                var inputStream = t.Item2;
                var ex = new Exception("Stream failed.");

                probe.SendNext(_byteString);
                sinkProbe.ExpectMsg<GraphStageMessages.Push>();

                var r = ReadN(inputStream, _byteString.Length);
                r.Item1.Should().Be(_byteString.Length);
                r.Item2.Span.SequenceEqual(_byteString.Span).Should().BeTrue();

                probe.SendError(ex);
                sinkProbe.ExpectMsg<GraphStageMessages.Failure>().Ex.Should().Be(ex);

                var task = Task.Run(() => inputStream.ReadByte());

                Action block = () => task.Wait(Timeout);
                block.Should().Throw<Exception>();

                task.Exception.InnerException.Should().Be(ex);
                return Task.CompletedTask;
            }, _materializer);
        }

        [Fact]
        public async Task InputStreamSink_should_use_dedicated_default_blocking_io_dispatcher_by_default()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var sys = ActorSystem.Create("InputStreamSink-testing", Utils.UnboundedMailboxConfig);
                var materializer = ActorMaterializer.Create(sys);
                try
                {
                    this.SourceProbe<ReadOnlyMemory<byte>>().RunWith(StreamConverters.AsInputStream(), materializer);
                    (materializer as ActorMaterializerImpl).Supervisor.Tell(StreamSupervisor.GetChildren.Instance, TestActor);
                    var children = ExpectMsg<StreamSupervisor.Children>().Refs;
                    var actorRef = children.First(c => c.Path.ToString().Contains("inputStreamSink"));
                    Utils.AssertDispatcher(actorRef, ActorAttributes.IODispatcher.Name);
                }
                finally
                {
                    Shutdown(sys);
                }

                return Task.CompletedTask;
            }, _materializer);
        }

        [Fact]
        public async Task InputStreamSink_should_work_when_more_bytes_pulled_from_input_stream_than_available()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var inputStream = Source.Single(_byteString).RunWith(StreamConverters.AsInputStream(), _materializer);

                var r = ReadN(inputStream, _byteString.Length * 2);
                r.Item1.Should().Be(_byteString.Length);
                r.Item2.Span.SequenceEqual(_byteString.Span).Should().BeTrue();

                inputStream.Dispose();
                return Task.CompletedTask;
            }, _materializer);
        }


        [Fact]
        public async Task InputStreamSink_should_read_next_byte_as_an_int_from_InputStream()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var bytes = (ReadOnlyMemory<byte>)new byte[] { 0, 100, 200, 255 };
                var inputStream = Source.Single(bytes).RunWith(StreamConverters.AsInputStream(), _materializer);

                Enumerable.Range(1, 5)
                    .Select(_ => inputStream.ReadByte())
                    .Should().BeEquivalentTo(new[] { 0, 100, 200, 255, -1 });

                inputStream.Dispose();
                return Task.CompletedTask;
            }, _materializer);
        }

        [Fact]
        public void InputStreamSink_should_fail_to_materialize_with_zero_sized_input_buffer()
        {
            Action a = () => Source.Single(_byteString).RunWith(StreamConverters.AsInputStream(Timeout).WithAttributes(Attributes.CreateInputBuffer(0, 0)), _materializer);
            a.Should().Throw<ArgumentException>();
            /*
            With Source.single we test the code path in which the sink
            itself throws an exception when being materialized. If
            Source.empty is used, the same exception is thrown by
            Materializer.
            */
        }

        [Fact]
        public void InputStreamSink_should_throw_from_inputstream_read_if_terminated_abruptly()
        {
            var materializer = ActorMaterializer.Create(Sys);
            var probe = this.CreatePublisherProbe<ReadOnlyMemory<byte>>();
            var inputStream = Source.FromPublisher(probe).RunWith(StreamConverters.AsInputStream(), materializer);
            materializer.Shutdown();

            inputStream.Invoking(i => i.ReadByte()).Should().Throw<AbruptTerminationException>();
        }

        private static ReadOnlyMemory<byte> RandomByteString(int size)
        {
            var a = new byte[size];
            new Random().NextBytes(a);
            return a.AsMemory();
        }

        private (int, ReadOnlyMemory<byte>) ReadN(Stream s, int n)
        {
            var buf = new byte[n];
// CA2022 - testing our own Stream.Read override in InputStreamAdapter
#pragma warning disable CA2022
            var r = s.Read(buf, 0, n);
#pragma warning restore CA2022
            return (r, buf.AsMemory(0, r));
        }

        private TestSinkStage<ReadOnlyMemory<byte>, Stream> TestSink(TestProbe probe)
            => TestSinkStage<ReadOnlyMemory<byte>, Stream>.Create(new InputStreamSinkStage(Timeout), probe);
    }
}
