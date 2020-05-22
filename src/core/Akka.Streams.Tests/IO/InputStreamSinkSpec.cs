//-----------------------------------------------------------------------
// <copyright file="InputStreamSinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.IO;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.IO
{
    public class InputStreamSinkSpec : AkkaSpec
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(300);
        private readonly ActorMaterializer _materializer;
        private readonly ByteString _byteString = RandomByteString(3);

        public InputStreamSinkSpec(ITestOutputHelper helper) : base(Utils.UnboundedMailboxConfig, helper)
        {
            Sys.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
            var settings = ActorMaterializerSettings.Create(Sys).WithDispatcher("akka.actor.default-dispatcher");
            _materializer = Sys.Materializer(settings);
        }

        [Fact]
        public void InputStreamSink_should_read_bytes_from_input_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var inputStream = Source.Single(_byteString).RunWith(StreamConverters.AsInputStream(), _materializer);
                var result = ReadN(inputStream, _byteString.Count);
                inputStream.Dispose();
                result.Item1.Should().Be(_byteString.Count);
                result.Item2.Should().BeEquivalentTo(_byteString);

            }, _materializer);
        }

        [Fact]
        public void InputStreamSink_should_read_bytes_correctly_if_requested_by_input_stream_not_in_chunk_size()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sinkProbe = CreateTestProbe();
                var byteString2 = RandomByteString(3);
                var inputStream = Source.From(new[] { _byteString, byteString2, null })
                    .RunWith(TestSink(sinkProbe), _materializer);

                sinkProbe.ExpectMsgAllOf(GraphStageMessages.Push.Instance, GraphStageMessages.Push.Instance);

                var result = ReadN(inputStream, 2);
                result.Item1.Should().Be(2);
                result.Item2.Should().BeEquivalentTo(_byteString.Slice(0, 2));

                result = ReadN(inputStream, 2);
                result.Item1.Should().Be(2);
                result.Item2.Should().BeEquivalentTo(Enumerable.Concat(_byteString.Slice(2), byteString2.Slice(0, 1)));

                result = ReadN(inputStream, 2);
                result.Item1.Should().Be(2);
                result.Item2.Should().BeEquivalentTo(byteString2.Slice(1));

                inputStream.Dispose();

            }, _materializer);
        }

        [Fact]
        public void InputStreamSink_should_return_less_than_was_expected_when_data_source_has_provided_some_but_not_enough_data()
        {
            this.AssertAllStagesStopped(() =>
            {
                var inputStream = Source.Single(_byteString).RunWith(StreamConverters.AsInputStream(), _materializer);

                var arr = new byte[_byteString.Count + 1];
                inputStream.Read(arr, 0, arr.Length).Should().Be(arr.Length - 1);
                inputStream.Dispose();
                ByteString.FromBytes(arr).ShouldBeEquivalentTo(Enumerable.Concat(_byteString, ByteString.FromBytes(new byte[] { 0 })));

            }, _materializer);
        }

        [Fact(Skip ="Racy in Linux")]
        public void InputStreamSink_should_block_read_until_get_requested_number_of_bytes_from_upstream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var run =
                    this.SourceProbe<ByteString>()
                        .ToMaterialized(StreamConverters.AsInputStream(), Keep.Both)
                        .Run(_materializer);
                var probe = run.Item1;
                var inputStream = run.Item2;
                var f = Task.Run(() => inputStream.Read(new byte[_byteString.Count], 0, _byteString.Count));

                f.Wait(Timeout).Should().BeFalse();

                probe.SendNext(_byteString);
                f.Wait(RemainingOrDefault).Should().BeTrue();
                f.Result.Should().Be(_byteString.Count);

                probe.SendComplete();
                inputStream.ReadByte().Should().Be(-1);
                inputStream.Dispose();

            }, _materializer);
        }

        [Fact]
        public void InputStreamSink_should_throw_error_when_reactive_stream_is_closed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = this.SourceProbe<ByteString>()
                        .ToMaterialized(StreamConverters.AsInputStream(), Keep.Both)
                        .Run(_materializer);
                var probe = t.Item1;
                var inputStream = t.Item2;

                probe.SendNext(_byteString);
                inputStream.Dispose();
                probe.ExpectCancellation();

                Action block = () => inputStream.Read(new byte[1], 0, 1);
                block.ShouldThrow<IOException>();
            }, _materializer);
        }

        [Fact]
        public void InputStreamSink_should_return_all_data_when_upstream_is_completed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sinkProbe = CreateTestProbe();
                var t = this.SourceProbe<ByteString>().ToMaterialized(TestSink(sinkProbe), Keep.Both).Run(_materializer);
                var probe = t.Item1;
                var inputStream = t.Item2;
                var bytes = RandomByteString(1);

                probe.SendNext(bytes);
                sinkProbe.ExpectMsg<GraphStageMessages.Push>();

                probe.SendComplete();
                sinkProbe.ExpectMsg<GraphStageMessages.UpstreamFinish>();

                var result = ReadN(inputStream, 3);
                result.Item1.Should().Be(1);
                result.Item2.Should().BeEquivalentTo(bytes);
            }, _materializer);
        }

        [Fact]
        public void InputStreamSink_should_work_when_read_chunks_smaller_then_stream_chunks()
        {
            this.AssertAllStagesStopped(() =>
            {
                var bytes = RandomByteString(10);
                var inputStream = Source.Single(bytes).RunWith(StreamConverters.AsInputStream(), _materializer);

                while (!bytes.IsEmpty)
                {
                    var expected = bytes.Slice(0, 3);
                    bytes = bytes.Slice(3);

                    var result = ReadN(inputStream, 3);
                    result.Item1.Should().Be(expected.Count);
                    result.Item2.ShouldBeEquivalentTo(expected);
                }

                inputStream.Dispose();
            }, _materializer);
        }

        [Fact]
        public void InputStreamSink_should_throw_exception_when_call_read_With_wrong_parameters()
        {
            this.AssertAllStagesStopped(() =>
            {
                var inputStream = Source.Single(_byteString).RunWith(StreamConverters.AsInputStream(), _materializer);
                var buf = new byte[3];

                Action(() => inputStream.Read(buf, -1, 2)).ShouldThrow<ArgumentException>();
                Action(() => inputStream.Read(buf, 0, 5)).ShouldThrow<ArgumentException>();
                Action(() => inputStream.Read(new byte[0], 0, 1)).ShouldThrow<ArgumentException>();
                Action(() => inputStream.Read(buf, 0, 0)).ShouldThrow<ArgumentException>();
            }, _materializer);
        }

        private Action Action(Action a) => a;

        [Fact]
        public void InputStreamSink_should_successfully_read_several_chunks_at_once()
        {
            this.AssertAllStagesStopped(() =>
            {
                var bytes = Enumerable.Range(1, 4).Select(_ => RandomByteString(4)).ToList();
                var sinkProbe = CreateTestProbe();
                var inputStream = Source.From(bytes).RunWith(TestSink(sinkProbe), _materializer);

                //need to wait while all elements arrive to sink
                bytes.ForEach(_ => sinkProbe.ExpectMsg<GraphStageMessages.Push>());

                for (var i = 0; i < 2; i++)
                {
                    var r = ReadN(inputStream, 8);
                    r.Item1.Should().Be(8);
                    r.Item2.ShouldBeEquivalentTo(Enumerable.Concat(bytes[i * 2], bytes[i * 2 + 1]));
                }

                inputStream.Dispose();
            }, _materializer);
        }

        [Fact]
        public void InputStreamSink_should_work_when_read_chunks_bigger_than_stream_chunks()
        {
            this.AssertAllStagesStopped(() =>
            {
                var bytes1 = RandomByteString(10);
                var bytes2 = RandomByteString(10);
                var sinkProbe = CreateTestProbe();
                var inputStream = Source.From(new[] { bytes1, bytes2, null }).RunWith(TestSink(sinkProbe), _materializer);

                //need to wait while both elements arrive to sink
                sinkProbe.ExpectMsgAllOf(GraphStageMessages.Push.Instance, GraphStageMessages.Push.Instance);

                var r1 = ReadN(inputStream, 15);
                r1.Item1.Should().Be(15);
                r1.Item2.ShouldBeEquivalentTo(Enumerable.Concat(bytes1, bytes2.Slice(0, 5)));

                var r2 = ReadN(inputStream, 15);
                r2.Item1.Should().Be(5);
                r2.Item2.ShouldBeEquivalentTo(bytes2.Slice(5));

                inputStream.Dispose();
            }, _materializer);
        }

        [Fact]
        public void InputStreamSink_should_return_minus_1_when_read_after_stream_is_completed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var inputStream = Source.Single(_byteString).RunWith(StreamConverters.AsInputStream(), _materializer);

                var r = ReadN(inputStream, _byteString.Count);
                r.Item1.Should().Be(_byteString.Count);
                r.Item2.ShouldBeEquivalentTo(_byteString);

                inputStream.ReadByte().Should().Be(-1);
                inputStream.Dispose();
            }, _materializer);
        }

        [Fact]
        public void InputStreamSink_should_return_Exception_when_stream_is_failed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sinkProbe = CreateTestProbe();
                var t = this.SourceProbe<ByteString>().ToMaterialized(TestSink(sinkProbe), Keep.Both).Run(_materializer);
                var probe = t.Item1;
                var inputStream = t.Item2;
                var ex = new Exception("Stream failed.");

                probe.SendNext(_byteString);
                sinkProbe.ExpectMsg<GraphStageMessages.Push>();

                var r = ReadN(inputStream, _byteString.Count);
                r.Item1.Should().Be(_byteString.Count);
                r.Item2.ShouldBeEquivalentTo(_byteString);

                probe.SendError(ex);
                sinkProbe.ExpectMsg<GraphStageMessages.Failure>().Ex.Should().Be(ex);

                var task = Task.Run(() => inputStream.ReadByte());

                Action block = () => task.Wait(Timeout);
                block.ShouldThrow<Exception>();

                task.Exception.InnerException.Should().Be(ex);

            }, _materializer);
        }

        [Fact]
        public void InputStreamSink_should_use_dedicated_default_blocking_io_dispatcher_by_default()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sys = ActorSystem.Create("dispatcher-testing", Utils.UnboundedMailboxConfig);
                var materializer = ActorMaterializer.Create(sys);
                try
                {
                    this.SourceProbe<ByteString>().RunWith(StreamConverters.AsInputStream(), materializer);
                    (materializer as ActorMaterializerImpl).Supervisor.Tell(StreamSupervisor.GetChildren.Instance, TestActor);
                    var children = ExpectMsg<StreamSupervisor.Children>().Refs;
                    var actorRef = children.First(c => c.Path.ToString().Contains("inputStreamSink"));
                    Utils.AssertDispatcher(actorRef, "akka.stream.default-blocking-io-dispatcher");
                }
                finally
                {
                    Shutdown(sys);
                }
            }, _materializer);
        }

        [Fact]
        public void InputStreamSink_should_work_when_more_bytes_pulled_from_input_stream_than_available()
        {
            this.AssertAllStagesStopped(() =>
            {
                var inputStream = Source.Single(_byteString).RunWith(StreamConverters.AsInputStream(), _materializer);

                var r = ReadN(inputStream, _byteString.Count * 2);
                r.Item1.Should().Be(_byteString.Count);
                r.Item2.ShouldBeEquivalentTo(_byteString);

                inputStream.Dispose();
            }, _materializer);
        }


        [Fact]
        public void InputStreamSink_should_read_next_byte_as_an_int_from_InputStream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var bytes = ByteString.CopyFrom(new byte[] { 0, 100, 200, 255 });
                var inputStream = Source.Single(bytes).RunWith(StreamConverters.AsInputStream(), _materializer);

                Enumerable.Range(1, 5)
                    .Select(_ => inputStream.ReadByte())
                    .ShouldBeEquivalentTo(new[] { 0, 100, 200, 255, -1 });

                inputStream.Dispose();
            }, _materializer);
        }

        [Fact]
        public void InputStreamSink_should_fail_to_materialize_with_zero_sized_input_buffer()
        {
            Action a = () => Source.Single(_byteString).RunWith(StreamConverters.AsInputStream(Timeout).WithAttributes(Attributes.CreateInputBuffer(0, 0)), _materializer);
            a.ShouldThrow<ArgumentException>();
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
            var probe = this.CreatePublisherProbe<ByteString>();
            var inputStream = Source.FromPublisher(probe).RunWith(StreamConverters.AsInputStream(), materializer);
            materializer.Shutdown();

            inputStream.Invoking(i => i.ReadByte()).ShouldThrow<AbruptTerminationException>();
        }

        private static ByteString RandomByteString(int size)
        {
            var a = new byte[size];
            new Random().NextBytes(a);
            return ByteString.FromBytes(a);
        }

        private (int, ByteString) ReadN(Stream s, int n)
        {
            var buf = new byte[n];
            var r = s.Read(buf, 0, n);
            return (r, ByteString.FromBytes(buf, 0, r));
        }

        private TestSinkStage<ByteString, Stream> TestSink(TestProbe probe)
            => TestSinkStage<ByteString, Stream>.Create(new InputStreamSinkStage(Timeout), probe);
    }
}
