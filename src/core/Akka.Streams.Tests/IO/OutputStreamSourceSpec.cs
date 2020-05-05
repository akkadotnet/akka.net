//-----------------------------------------------------------------------
// <copyright file="OutputStreamSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.IO;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.IO
{
    public class OutputStreamSourceSpec : AkkaSpec
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

        private readonly ActorMaterializer _materializer;
        private readonly byte[] _bytesArray;
        private readonly ByteString _byteString;

        public OutputStreamSourceSpec(ITestOutputHelper helper) : base(Utils.UnboundedMailboxConfig, helper)
        {
            Sys.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
            var settings = ActorMaterializerSettings.Create(Sys).WithDispatcher("akka.actor.default-dispatcher");
            _materializer = Sys.Materializer(settings);

            _bytesArray = new[]
            {
                Convert.ToByte(new Random().Next(256)),
                Convert.ToByte(new Random().Next(256)),
                Convert.ToByte(new Random().Next(256))
            };

            _byteString = ByteString.FromBytes(_bytesArray);
        }

        private void ExpectTimeout(Task f, TimeSpan duration) => f.Wait(duration).Should().BeFalse();

        private void ExpectSuccess<T>(Task<T> f, T value)
        {
            f.Wait(); // just let it run
            f.Result.Should().Be(value);
        }

        [Fact]
        public void OutputStreamSource_must_read_bytes_from_OutputStream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = StreamConverters.AsOutputStream()
                        .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                        .Run(_materializer);
                var outputStream = t.Item1;
                var probe = t.Item2;
                var s = probe.ExpectSubscription();

                outputStream.Write(_bytesArray, 0, _bytesArray.Length);
                s.Request(1);
                probe.ExpectNext(_byteString);
                outputStream.Dispose();
                probe.ExpectComplete();
            }, _materializer);
        }

        [Fact]
        public void OutputStreamSource_must_block_flush_call_until_send_all_buffer_to_downstream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = StreamConverters.AsOutputStream()
                        .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                        .Run(_materializer);
                var outputStream = t.Item1;
                var probe = t.Item2;
                var s = probe.ExpectSubscription();

                outputStream.Write(_bytesArray, 0, _bytesArray.Length);
                var f = Task.Run(() =>
                {
                    outputStream.Flush();
                    return NotUsed.Instance;
                });

                ExpectTimeout(f, Timeout);
                probe.ExpectNoMsg(TimeSpan.MinValue);

                s.Request(1);
                ExpectSuccess(f, NotUsed.Instance);
                probe.ExpectNext(_byteString);

                outputStream.Dispose();
                probe.ExpectComplete();
            }, _materializer);
        }

        [Fact]
        public void OutputStreamSource_must_not_block_flushes_when_buffer_is_empty()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = StreamConverters.AsOutputStream()
                        .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                        .Run(_materializer);
                var outputStream = t.Item1;
                var probe = t.Item2;
                var s = probe.ExpectSubscription();

                outputStream.Write(_bytesArray, 0, _byteString.Count);
                var f = Task.Run(() =>
                {
                    outputStream.Flush();
                    return NotUsed.Instance;
                });
                s.Request(1);
                ExpectSuccess(f, NotUsed.Instance);
                probe.ExpectNext(_byteString);

                var f2 = Task.Run(() =>
                {
                    outputStream.Flush();
                    return NotUsed.Instance;
                });
                ExpectSuccess(f2, NotUsed.Instance);

                outputStream.Dispose();
                probe.ExpectComplete();

            }, _materializer);
        }
        
        [Fact]
        public void OutputStreamSource_must_block_writes_when_buffer_is_full()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = StreamConverters.AsOutputStream()
                    .WithAttributes(Attributes.CreateInputBuffer(16, 16))
                    .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                    .Run(_materializer);
                var outputStream = t.Item1;
                var probe = t.Item2;
                var s = probe.ExpectSubscription();

                for (var i = 1; i <= 16; i++)
                    outputStream.Write(_bytesArray, 0, _byteString.Count);

                //blocked call
                var f = Task.Run(() =>
                {
                    outputStream.Write(_bytesArray, 0, _byteString.Count);
                    return NotUsed.Instance;
                });
                ExpectTimeout(f, Timeout);
                probe.ExpectNoMsg(TimeSpan.MinValue);

                s.Request(17);
                ExpectSuccess(f, NotUsed.Instance);
                probe.ExpectNextN(Enumerable.Repeat(_byteString, 17).ToList());

                outputStream.Dispose();
                probe.ExpectComplete();
            }, _materializer);
        }

        [Fact]
        public void OutputStreamSource_must_throw_error_when_writer_after_stream_is_closed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = StreamConverters.AsOutputStream()
                        .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                        .Run(_materializer);
                var outputStream = t.Item1;
                var probe = t.Item2;

                probe.ExpectSubscription();
                outputStream.Dispose();
                probe.ExpectComplete();

                outputStream.Invoking(s => s.Write(_bytesArray, 0, _byteString.Count)).ShouldThrow<IOException>();
            }, _materializer);
        }

        [Fact]
        public void OutputStreamSource_must_use_dedicated_default_blocking_io_dispatcher_by_default()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sys = ActorSystem.Create("dispatcher-testing", Utils.UnboundedMailboxConfig);
                var materializer = sys.Materializer();

                try
                {
                    StreamConverters.AsOutputStream().RunWith(this.SinkProbe<ByteString>(), materializer);
                    ((ActorMaterializerImpl) materializer).Supervisor.Tell(StreamSupervisor.GetChildren.Instance,
                        TestActor);
                    var actorRef = ExpectMsg<StreamSupervisor.Children>()
                            .Refs.First(c => c.Path.ToString().Contains("outputStreamSource"));
                    Utils.AssertDispatcher(actorRef, "akka.stream.default-blocking-io-dispatcher");
                }
                finally
                {
                    Shutdown(sys);
                }

            }, _materializer);
        }

        [Fact]
        public void OutputStreamSource_must_throw_IOException_when_writing_to_the_stream_after_the_subscriber_has_cancelled_the_reactive_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sourceProbe = CreateTestProbe();
                var t =
                    TestSourceStage<ByteString, Stream>.Create(new OutputStreamSourceStage(Timeout), sourceProbe)
                        .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                        .Run(_materializer);
                var outputStream = t.Item1;
                var probe = t.Item2;

                var s = probe.ExpectSubscription();

                outputStream.Write(_bytesArray, 0, _bytesArray.Length);
                s.Request(1);
                sourceProbe.ExpectMsg<GraphStageMessages.Pull>();

                probe.ExpectNext(_byteString);

                s.Cancel();
                sourceProbe.ExpectMsg<GraphStageMessages.DownstreamFinish>();

                Thread.Sleep(500);
                outputStream.Invoking(os => os.Write(_bytesArray, 0, _bytesArray.Length)).ShouldThrow<IOException>();
            }, _materializer);
        }

        [Fact]
        public void OutputStreamSource_must_fail_to_materialize_with_zero_sized_input_buffer()
        {
            new Action(
                () =>
                    StreamConverters.AsOutputStream(Timeout)
                        .WithAttributes(Attributes.CreateInputBuffer(0, 0))
                        .RunWith(Sink.First<ByteString>(), _materializer)).ShouldThrow<ArgumentException>();
            /*
             With Sink.First we test the code path in which the source
             itself throws an exception when being materialized. If
             Sink.Ignore is used, the same exception is thrown by
             Materializer.
             */
        }

        [Fact]
        public void OutputStreamSource_must_not_leave_blocked_threads()
        {
            var tuple =
                StreamConverters.AsOutputStream(Timeout)
                    .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                    .Run(_materializer);
            var outputStream = tuple.Item1;
            var probe = tuple.Item2;

            var sub = probe.ExpectSubscription();

            // triggers a blocking read on the queue
            // and then cancel the stage before we got anything
            sub.Request(1);
            sub.Cancel();

            //we need to make sure that the underling BlockingCollection isn't blocked after the stream has finished, 
            //the jvm way isn't working so we need to use reflection and check the collection directly
            //def threadsBlocked =
            //ManagementFactory.getThreadMXBean.dumpAllThreads(true, true).toSeq
            //          .filter(t => t.getThreadName.startsWith("OutputStreamSourceSpec") &&
            //t.getLockName != null &&
            //t.getLockName.startsWith("java.util.concurrent.locks.AbstractQueuedSynchronizer"))
            //awaitAssert(threadsBlocked should === (Seq()), 3.seconds)

            var bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Static;
            var field = typeof(OutputStreamAdapter).GetField("_dataQueue", bindFlags);
            var blockingCollection = field.GetValue(outputStream) as BlockingCollection<ByteString>;

            //give the stage enough time to finish, otherwise it may take the hello message
            Thread.Sleep(1000);

            // if a take operation is pending inside the stage it will steal this one and the next take will not succeed
            blockingCollection.Add(ByteString.FromString("hello"));

            ByteString result;
            blockingCollection.TryTake(out result, TimeSpan.FromSeconds(3)).Should().BeTrue();
            result.ToString().Should().Be("hello");
        }

        [Fact]
        public void OutputStreamSource_must_correctly_complete_the_stage_after_close()
        {
            // actually this was a race, so it only happened in at least one of 20 runs

            const int bufferSize = 4;

            var t = StreamConverters.AsOutputStream(Timeout)
                .AddAttributes(Attributes.CreateInputBuffer(bufferSize, bufferSize))
                .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                .Run(_materializer);
            var outputStream = t.Item1;
            var probe = t.Item2;

            // fill the buffer up
            Enumerable.Range(1, bufferSize - 1).ForEach(i => outputStream.WriteByte((byte)i));

            Task.Run(() => outputStream.Dispose());

            // here is the race, has the elements reached the stage buffer yet?
            Thread.Sleep(500);
            probe.Request(bufferSize - 1);
            probe.ExpectNextN(bufferSize - 1);
            probe.ExpectComplete();
        }
    }
}
