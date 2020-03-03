//-----------------------------------------------------------------------
// <copyright file="FilePublisherTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Reactive.Streams;

namespace Akka.Streams.Tests.TCK
{
    class FilePublisherTest : AkkaPublisherVerification<ByteString>
    {
        private const int ChunkSize = 256;
        private const int Elements = 1000;

        private static int _counter;
        private readonly List<FileInfo> _files = new List<FileInfo>();

        public FilePublisherTest() : base(Utils.UnboundedMailboxConfig.WithFallback(AkkaSpec.AkkaSpecConfig))
        {
        }

        protected override void AfterShutdown() => _files.ForEach(f => f.Delete());

        public override IPublisher<ByteString> CreatePublisher(long elements)
        {
            var file = new FileInfo(Path.Combine(Path.GetTempPath(), $"file-source-tck-{_counter++}.tmp"));
            _files.Add(file);

            var chunk = Enumerable.Range(1, ChunkSize).Select(_ => "x").Aggregate("", (s, s1) => s + s1);
            using (var writer = file.CreateText())
                for (var i = 0; i < Elements; i++)
                    writer.Write(chunk);

            return FileIO.FromFile(file, 512)
                .Take(elements)
                .RunWith(Sink.AsPublisher<ByteString>(false), Materializer);
        }

        public override long MaxElementsFromPublisher { get; } = Elements;
    }
}
