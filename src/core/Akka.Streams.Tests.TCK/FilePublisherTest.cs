//-----------------------------------------------------------------------
// <copyright file="FilePublisherTest.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.IO;
using System.Linq;
using Akka.Dispatch;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit.Tests;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK
{
    class FilePublisherTest : AkkaPublisherVerification<ByteString>
    {
        private const int ChunkSize = 256;
        private const int Elements = 1000;

        private FileInfo _file;

        public FilePublisherTest() : base(Utils.UnboundedMailboxConfig.WithFallback(AkkaSpec.TestConfig))
        {
            _file = new FileInfo(Path.Combine(Path.GetTempPath(), "file-source-tck.tmp"));

            var chunk = Enumerable.Range(1, ChunkSize).Select(_ => "x").Aggregate("", (s, s1) => s + s1);
            using (var writer = _file.CreateText()) 
                for (var i = 0; i < Elements; i++)
                    writer.Write(chunk);
        }

        protected override void AfterShutdown() => _file.Delete();

        public override IPublisher<ByteString> CreatePublisher(long elements)
            => FileIO.FromFile(_file, 512)
                .Take(elements)
                .RunWith(Sink.AsPublisher<ByteString>(false), Materializer);

        public override long MaxElementsFromPublisher { get; } = Elements;
    }
}
