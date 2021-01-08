//-----------------------------------------------------------------------
// <copyright file="InputStreamSourceTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.IO;
using Akka.IO;
using Akka.Streams.Dsl;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK
{
    class InputStreamSourceTest : AkkaPublisherVerification<ByteString>
    {
        public override IPublisher<ByteString> CreatePublisher(long elements)
        {
            return StreamConverters.FromInputStream(() => new InputStream())
                .WithAttributes(ActorAttributes.CreateDispatcher("akka.test.stream-dispatcher"))
                .Take(elements)
                .RunWith(Sink.AsPublisher<ByteString>(false), Materializer);
        }

        private sealed class InputStream : Stream
        {
            private byte _num;

            public override void Flush()
            {
                throw new System.NotImplementedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new System.NotImplementedException();
            }

            public override void SetLength(long value)
            {
                throw new System.NotImplementedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                buffer[offset] = _num++;
                return 1;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new System.NotImplementedException();
            }

            public override bool CanRead { get; }
            public override bool CanSeek { get; } = false;
            public override bool CanWrite { get; } = false;
            public override long Length { get; }
            public override long Position { get; set; }
        }
    }
}
