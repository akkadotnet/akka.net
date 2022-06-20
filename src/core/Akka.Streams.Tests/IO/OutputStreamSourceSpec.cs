//-----------------------------------------------------------------------
// <copyright file="OutputStreamSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.IO;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Extensions;
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

        public OutputStreamSourceSpec(ITestOutputHelper helper) : base(  
            ConfigurationFactory.ParseString("akka.loglevel = DEBUg").WithFallback(Utils.UnboundedMailboxConfig), 
            helper)
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

        private static async Task ExpectTimeout(Task f, TimeSpan duration) => 
            (await f.AwaitWithTimeout(duration)).Should().BeFalse();

        [Fact]
        public async Task OutputStreamSource_must_read_bytes_from_OutputStream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (outputStream, probe) = StreamConverters.AsOutputStream()
                    .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                    .Run(_materializer);
                var s = await probe.ExpectSubscriptionAsync();

                await outputStream.WriteAsync(_bytesArray, 0, _bytesArray.Length)
                    .ShouldCompleteWithin(Timeout);
                s.Request(1);
                await probe.ExpectNextAsync(_byteString);
                outputStream.Dispose();
                await probe.ExpectCompleteAsync();
            }, _materializer);
        }

        [Fact]
        public async Task OutputStreamSource_must_block_flush_call_until_send_all_buffer_to_downstream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (outputStream, probe) = StreamConverters.AsOutputStream()
                    .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                    .Run(_materializer);

                using (outputStream)
                {
                    var s = await probe.ExpectSubscriptionAsync();
                    
                    await outputStream.WriteAsync(_bytesArray, 0, _bytesArray.Length)
                        .ShouldCompleteWithin(Timeout);
                    var f = outputStream.FlushAsync();

                    await ExpectTimeout(f, Timeout);
                    await probe.ExpectNoMsgAsync(TimeSpan.MinValue);

                    s.Request(1);
                    await f.ShouldCompleteWithin(Timeout);
                    await probe.AsyncBuilder().ExpectNext(_byteString).ExecuteAsync();
                }

                await probe.AsyncBuilder().ExpectComplete().ExecuteAsync();
            }, _materializer);
        }

        [Fact]
        public async Task OutputStreamSource_must_not_block_flushes_when_buffer_is_empty()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (outputStream, probe) = StreamConverters.AsOutputStream()
                    .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                    .Run(_materializer);
                
                using (outputStream)
                {
                    var s = await probe.ExpectSubscriptionAsync();

                    await outputStream.WriteAsync(_bytesArray, 0, _byteString.Count)
                        .ShouldCompleteWithin(Timeout);
                    var f = outputStream.FlushAsync();
                    s.Request(1);
                    await f.ShouldCompleteWithin(Timeout);
                    await probe.ExpectNextAsync(_byteString);

                    var f2 = outputStream.FlushAsync();
                    await f2.ShouldCompleteWithin(Timeout);
                }

                await probe.ExpectCompleteAsync();

            }, _materializer);
        }
        
        [Fact]
        public async Task OutputStreamSource_must_block_writes_when_buffer_is_full()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (outputStream, probe) = StreamConverters.AsOutputStream()
                    .WithAttributes(Attributes.CreateInputBuffer(16, 16))
                    .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                    .Run(_materializer);
                using (outputStream)
                {
                    var s = await probe.ExpectSubscriptionAsync();

                    foreach (var _ in Enumerable.Range(1, 16))
                        await outputStream.WriteAsync(_bytesArray, 0, _byteString.Count)
                            .ShouldCompleteWithin(Timeout);

                    //blocked call
                    var f = outputStream.WriteAsync(_bytesArray, 0, _byteString.Count);

                    await ExpectTimeout(f, Timeout);
                    await probe.ExpectNoMsgAsync(TimeSpan.MinValue);

                    s.Request(17);
                    await f.ShouldCompleteWithin(Timeout);
                    await probe.ExpectNextNAsync(Enumerable.Repeat(_byteString, 17).ToList());
                }

                await probe.ExpectCompleteAsync();
            }, _materializer);
        }

        [Fact]
        public async Task OutputStreamSource_must_throw_error_when_writer_after_stream_is_closed()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (outputStream, probe) = StreamConverters.AsOutputStream()
                    .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                    .Run(_materializer);

                await probe.ExpectSubscriptionAsync();
                outputStream.Dispose();
                await probe.ExpectCompleteAsync();

                await outputStream.WriteAsync(_bytesArray, 0, _byteString.Count)
                    .ShouldThrowWithin<IOException>(Timeout);
            }, _materializer);
        }

        [Fact]
        public async Task OutputStreamSource_must_use_dedicated_default_blocking_io_dispatcher_by_default()
        {
            var sys = ActorSystem.Create("dispatcher-testing", Utils.UnboundedMailboxConfig.WithFallback(DefaultConfig));
            var materializer = sys.Materializer();
            try
            {
                await this.AssertAllStagesStoppedAsync(async () =>
                {
                    StreamConverters.AsOutputStream().RunWith(this.SinkProbe<ByteString>(), materializer);
                    ((ActorMaterializerImpl) materializer).Supervisor.Tell(StreamSupervisor.GetChildren.Instance,
                        TestActor);
                    var actorRef = (await ExpectMsgAsync<StreamSupervisor.Children>())
                        .Refs.First(c => c.Path.ToString().Contains("outputStreamSource"));
                    Utils.AssertDispatcher(actorRef, ActorAttributes.IODispatcher.Name);
                }, materializer);
            }
            finally
            {
                await ShutdownAsync(sys);
            }
        }

        [Fact]
        public async Task OutputStreamSource_must_throw_IOException_when_writing_to_the_stream_after_the_subscriber_has_cancelled_the_reactive_stream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var sourceProbe = CreateTestProbe();
                var (outputStream, probe) = TestSourceStage<ByteString, Stream>.Create(new OutputStreamSourceStage(Timeout), sourceProbe)
                    .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                    .Run(_materializer);

                var s = await probe.ExpectSubscriptionAsync();

                await outputStream.WriteAsync(_bytesArray, 0, _bytesArray.Length)
                    .ShouldCompleteWithin(Timeout);
                s.Request(1);
                await sourceProbe.ExpectMsgAsync<GraphStageMessages.Pull>();

                await probe.ExpectNextAsync(_byteString);

                s.Cancel();
                await sourceProbe.ExpectMsgAsync<GraphStageMessages.DownstreamFinish>();

                await Task.Delay(500);
                await outputStream.WriteAsync(_bytesArray, 0, _bytesArray.Length)
                    .ShouldThrowWithin<IOException>(Timeout);
            }, _materializer);
        }

        [Fact]
        public void OutputStreamSource_must_fail_to_materialize_with_zero_sized_input_buffer()
        {
            new Action(
                () =>
                    StreamConverters.AsOutputStream(Timeout)
                        .WithAttributes(Attributes.CreateInputBuffer(0, 0))
                        .RunWith(Sink.First<ByteString>(), _materializer)).Should().Throw<ArgumentException>();
            /*
             With Sink.First we test the code path in which the source
             itself throws an exception when being materialized. If
             Sink.Ignore is used, the same exception is thrown by
             Materializer.
             */
        }

        [Fact]
        public async Task OutputStreamSource_must_not_leave_blocked_threads()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (outputStream, probe) = StreamConverters.AsOutputStream(Timeout)
                    .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                    .Run(_materializer);

                var sub = await probe.ExpectSubscriptionAsync();

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

                const BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                               BindingFlags.Static;
                var field = typeof(OutputStreamAdapter).GetField("_dataQueue", bindFlags);
                if (field == null)
                    throw new Exception($"Failed to retrieve the field `_dataQueue` from class {nameof(OutputStreamAdapter)}");
                var blockingCollection = (BlockingCollection<ByteString>) field.GetValue(outputStream);

                //give the stage enough time to finish, otherwise it may take the hello message
                await Task.Delay(1000);

                // if a take operation is pending inside the stage it will steal this one and the next take will not succeed
                blockingCollection.Add(ByteString.FromString("hello"));

                blockingCollection.TryTake(out var result, TimeSpan.FromSeconds(3)).Should().BeTrue();
                result.ToString().Should().Be("hello");
            }, _materializer);
        }

        [Fact(Skip = "Racy")]
        public async Task OutputStreamSource_must_correctly_complete_the_stage_after_close()
        {
            // actually this was a race, so it only happened in at least one of 20 runs

            const int bufferSize = 4;
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var (outputStream, probe) = StreamConverters.AsOutputStream(Timeout)
                    .AddAttributes(Attributes.CreateInputBuffer(bufferSize, bufferSize))
                    .ToMaterialized(this.SinkProbe<ByteString>(), Keep.Both)
                    .Run(_materializer);

                using (outputStream)
                {
                    // fill the buffer up
                    Enumerable.Range(1, bufferSize - 1).ForEach(i => outputStream.WriteByte((byte)i));
                }

                // here is the race, has the elements reached the stage buffer yet?
                await Task.Delay(500);
                probe.Request(bufferSize - 1);
                await probe.ExpectNextNAsync(bufferSize - 1).ToListAsync();
                await probe.ExpectCompleteAsync();
            }, _materializer);
        }
    }
}
