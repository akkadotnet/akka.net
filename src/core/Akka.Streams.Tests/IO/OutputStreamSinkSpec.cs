//-----------------------------------------------------------------------
// <copyright file="OutputStreamSinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.IO;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

namespace Akka.Streams.Tests.IO
{
    public class OutputStreamSinkSpec : AkkaSpec
    {
        #region Internal classes

        private sealed class VoidOutputStream : Stream
        {
            private readonly TestProbe _p;

            public VoidOutputStream(TestProbe p)
            {
                _p = p;
            }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
                => _p.Ref.Tell(ByteString.FromBytes(buffer, offset, count).ToString());

            public override bool CanRead { get; }
            public override bool CanSeek { get; }
            public override bool CanWrite { get; } = true;
            public override long Length { get; }
            public override long Position { get; set; }
        }

        private sealed class CloseOutputStream : Stream
        {
            private readonly TestProbe _p;

            public CloseOutputStream(TestProbe p)
            {
                _p = p;
            }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                _p.Ref.Tell("closed");
            }

            public override bool CanRead { get; }
            public override bool CanSeek { get; }
            public override bool CanWrite { get; } = true;
            public override long Length { get; }
            public override long Position { get; set; }
        }

        private sealed class CompletionOutputStream : Stream
        {
            private readonly TestProbe _p;

            public CompletionOutputStream(TestProbe p)
            {
                _p = p;
            }

            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
                => _p.Ref.Tell(ByteString.FromBytes(buffer, offset, count).ToString());

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                _p.Ref.Tell("closed");
            }

            public override bool CanRead { get; }
            public override bool CanSeek { get; }
            public override bool CanWrite { get; } = true;
            public override long Length { get; }
            public override long Position { get; set; }
        }

        private sealed class OutputStream : Stream
        {
            public override void Flush()
            {
                throw new NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
            }

            public override bool CanRead { get; }
            public override bool CanSeek { get; }
            public override bool CanWrite => true;
            public override long Length { get; }
            public override long Position { get; set; }
        }

        #endregion

        private readonly ActorMaterializer _materializer;

        public OutputStreamSinkSpec(ITestOutputHelper helper) : base(Utils.UnboundedMailboxConfig, helper)
        {
            Sys.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
            var settings = ActorMaterializerSettings.Create(Sys).WithDispatcher("akka.actor.default-dispatcher");
            _materializer = Sys.Materializer(settings);
        }

        [Fact]
        public async Task OutputStreamSink_must_write_bytes_to_void_OutputStream()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var p = CreateTestProbe();
                var datas = new List<ByteString>
                {
                    ByteString.FromString("a"),
                    ByteString.FromString("c"),
                    ByteString.FromString("c")
                };

                var completion = Source.From(datas)
                    .RunWith(StreamConverters.FromOutputStream(() => new VoidOutputStream(p)), _materializer);

                await p.ExpectMsgAsync(datas[0].ToString());
                await p.ExpectMsgAsync(datas[1].ToString());
                await p.ExpectMsgAsync(datas[2].ToString());
                await completion.ShouldCompleteWithin(3.Seconds());
            }, _materializer);
        }

        [Fact]
        public async Task OutputStreamSink_must_close_underlying_stream_when_error_received()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var p = CreateTestProbe();
                var completion = Source.Failed<ByteString>(new Exception("Boom!"))
                    .RunWith(StreamConverters.FromOutputStream(() => new CloseOutputStream(p)), _materializer);

                await p.ExpectMsgAsync("closed");
                await completion.ShouldThrowWithin<AbruptIOTerminationException>(3.Seconds());
            }, _materializer);
        }

        [Fact]
        public async Task OutputStreamSink_must_complete_materialized_value_with_the_error()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                await Awaiting(async () =>
                {
                    await Source.Failed<ByteString>(new Exception("Boom!"))
                        .RunWith(StreamConverters.FromOutputStream(() => new OutputStream()), _materializer)
                        .ShouldCompleteWithin(3.Seconds());
                }).Should().ThrowAsync<AbruptIOTerminationException>();
            }, _materializer);
        }

        [Fact]
        public async Task OutputStreamSink_must_close_underlying_stream_when_completion_received()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var p = CreateTestProbe();
                var completion = Source.Empty<ByteString>()
                    .RunWith(StreamConverters.FromOutputStream(() => new CompletionOutputStream(p)), _materializer);

                await p.ExpectMsgAsync("closed");
                await completion.ShouldCompleteWithin(3.Seconds());
            }, _materializer);
        }
    }
}
