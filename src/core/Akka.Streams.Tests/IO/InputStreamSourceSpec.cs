//-----------------------------------------------------------------------
// <copyright file="InputStreamSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.IO
{
    public class InputStreamSourceSpec : AkkaSpec
    {
        #region internal classes 

        private sealed class ConstInputStream : Stream
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
                buffer[0] = 42;
                return 1;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override bool CanRead { get; } = true;
            public override bool CanSeek { get; }
            public override bool CanWrite { get; }
            public override long Length { get; }
            public override long Position { get; set; }
        }

        private sealed class ListInputStream : Stream
        {
            private List<byte> _buf;

            public ListInputStream(IEnumerable<string> values)
            {
                _buf = values.Select(s => Convert.ToByte(s[0])).ToList();
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
                if (_buf.Count == 0)
                    return 0;

                _buf.CopyTo(buffer);
                _buf = new List<byte>();
                return 3;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override bool CanRead { get; } = true;
            public override bool CanSeek { get; }
            public override bool CanWrite { get; }
            public override long Length { get; }
            public override long Position { get; set; }
        }

        private sealed class EmittedInputStream : Stream
        {
            private readonly TestLatch _latch;
            private bool _emitted;

            public EmittedInputStream(TestLatch latch)
            {
                _latch = latch;
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
                if (!_emitted)
                {
                    _emitted = true;
                    buffer[0] = Convert.ToByte('M');
                    return 1;
                }

                _latch.Ready();
                return 0;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }

            public override bool CanRead { get; } = true;
            public override bool CanSeek { get; }
            public override bool CanWrite { get; }
            public override long Length { get; }
            public override long Position { get; set; }
        }

        #endregion

        private readonly ActorMaterializer _materializer;

        public InputStreamSourceSpec(ITestOutputHelper helper) : base(Utils.UnboundedMailboxConfig, helper)
        {
            Sys.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
            var settings = ActorMaterializerSettings.Create(Sys).WithDispatcher("akka.actor.default-dispatcher");
            _materializer = Sys.Materializer(settings);
        }

        [Fact]
        public void InputStreamSource_must_not_signal_when_no_demand()
        {
            var f = StreamConverters.FromInputStream(() => new ConstInputStream());

            f.TakeWithin(TimeSpan.FromSeconds(5)).RunForeach(it => { }, _materializer).Wait(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public void InputStreamSource_must_read_bytes_from_InputStream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var f = StreamConverters.FromInputStream(() => new ListInputStream(new[] {"a", "b", "c"}))
                    .RunWith(Sink.First<ByteString>(), _materializer);

                f.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                f.Result.ShouldBeEquivalentTo(ByteString.FromString("abc"));
            }, _materializer);
        }

        [Fact]
        public void InputStreamSource_must_emit_as_soon_as_read()
        {
            this.AssertAllStagesStopped(() =>
            {
                var latch = new TestLatch(1);
                var probe = StreamConverters.FromInputStream(() => new EmittedInputStream(latch), chunkSize: 1)
                    .RunWith(this.SinkProbe<ByteString>(), _materializer);

                probe.Request(4);
                probe.ExpectNext(ByteString.FromString("M"));
                latch.CountDown();
                probe.ExpectComplete();
            }, _materializer);
        }
    }
}
