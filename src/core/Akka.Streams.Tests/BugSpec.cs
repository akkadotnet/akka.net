//-----------------------------------------------------------------------
// <copyright file="BugSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.IO;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests
{
    public class BugSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public BugSpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public async Task Issue_4580_EmptyByteStringCausesPipeToBeClosed()
        {
            var serverPipe = new NamedPipeServerStream("unique-pipe-name", PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 20, 20);
            var serverPipeConnectionTask = serverPipe.WaitForConnectionAsync();

            var clientPipe = new NamedPipeClientStream(".", "unique-pipe-name", PipeDirection.In, PipeOptions.Asynchronous);
            clientPipe.Connect();

            await serverPipeConnectionTask;
            var cnt = 0;

            var writeToStreamTask = Source.From(Enumerable.Range(0, 100))
                .Select(i => ByteString.FromString(i.ToString()))
                .Select(bs => cnt++ == 10 ? ByteString.Empty : bs) // ByteString.Empty.ToArray() failed in original bug
                .ToMaterialized(StreamConverters.FromOutputStream(() => serverPipe), Keep.Right)
                .Run(Materializer);

            var result = new List<string>();
            var readFromStreamTask = StreamConverters.FromInputStream(() => clientPipe, 1)
                .RunForeach(bs => result.Add(bs.ToString(Encoding.ASCII)), Materializer);

            await Task.WhenAll(writeToStreamTask, readFromStreamTask);

            var expected = Enumerable.Range(0, 100)
                .SelectMany(i => i == 10 ? Array.Empty<string>() : i.ToString().Select(c => c.ToString()));
            expected.SequenceEqual(result).ShouldBeTrue();
        }
    }
}
